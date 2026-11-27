/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Server._ST14.ZLevels.Core;
using Content.Server.Administration;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.Administration;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Trenches on the layers of a sector. Digging a tile of a level opens it into the level below; down there the tile
/// becomes the floor of the trench and every untouched neighbour gets an earth wall, so a trench is a corridor you can
/// only walk along. Steps (a ladder) lead out, or a soldier climbs over the side. The entrenching tool digs whole
/// segments, see ST14TrenchSystem.Digging.cs. A soldier in the trench is a level lower: the ground of the level above
/// stands between them and the fire on the surface, and the trench is open to the sky only where it was dug.
/// </summary>
public sealed partial class ST14TrenchSystem : EntitySystem
{
    public static readonly EntProtoId TrenchWall = "ST14TrenchWall";
    public static readonly EntProtoId TrenchSteps = "ST14TrenchSteps";
    public static readonly EntProtoId BunkerRoof = "ST14BunkerRoof";
    public static readonly EntProtoId DugoutRoof = "ST14DugoutRoof";
    public static readonly ProtoId<ContentTileDefinition> TrenchFloor = "FloorDirt";

    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;
    [Dependency] private Content.Server._ST14.ZLevels.Roof.ST14ZLevelsRoofSystem _roofs = default!;


    /// <summary>
    /// Opens a world tile of a level into the level below and makes it a trench there. Untouched neighbours of the
    /// trench get earth walls on the level below; a wall in the way of the trench goes
    /// </summary>
    /// <param name="from">
    /// Where the digger stands. A new trench gets steps on its first tile leading up to the digger, and digging the
    /// tile the steps lead to moves them on along the trench, so a trench always has a way in and out.
    /// </param>
    public bool DigTrench(EntityUid map, Vector2i worldTile, EntityCoordinates? from = null)
    {
        if (!TryComp<ST14ZMapComponent>(map, out var zMap) ||
            !_zLevels.TryMapDown((map, zMap), out var below) ||
            !_zLevels.TryGetLayerGrid(map, out var ground) ||
            !_zLevels.TryGetLayerGrid(below, out var floor))
        {
            return false;
        }

        // a new trench: none of its four sides is dug yet
        var fresh = true;
        foreach (var side in Cardinals)
        {
            var groundSide = TileOf(ground.Value, worldTile + side);
            if (!_map.TryGetTileRef(ground.Value, ground.Value.Comp, groundSide, out var sideRef) || sideRef.Tile.IsEmpty)
                fresh = false;
        }

        // steps that led up onto this tile now lead into the hole: they move here and lead on the same way
        var movedSteps = TakeStepsLeadingTo(floor.Value, worldTile);

        // down there first, so whoever falls through lands on the floor of the trench
        var floorTile = new Tile(_tiles[TrenchFloor].TileId);
        var center = TileOf(floor.Value, worldTile);
        if (!_map.TryGetTileRef(floor.Value, floor.Value.Comp, center, out var under) || under.Tile.IsEmpty)
            _map.SetTile(floor.Value, floor.Value.Comp, center, floorTile);

        RemoveWalls(floor.Value, center);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                var neighbour = worldTile + new Vector2i(dx, dy);

                // the trench goes on there, no wall between the two
                var groundIndex = TileOf(ground.Value, neighbour);
                if (!_map.TryGetTileRef(ground.Value, ground.Value.Comp, groundIndex, out var above) || above.Tile.IsEmpty)
                    continue;

                var index = TileOf(floor.Value, neighbour);
                if (!_map.TryGetTileRef(floor.Value, floor.Value.Comp, index, out var side) || side.Tile.IsEmpty)
                    _map.SetTile(floor.Value, floor.Value.Comp, index, floorTile);

                if (!HasWall(floor.Value, index))
                    Spawn(TrenchWall, _map.GridTileToLocal(floor.Value, floor.Value.Comp, index));
            }
        }

        // the opening last: the tile change wakes and drops whoever stands on it
        _map.SetTile(ground.Value, ground.Value.Comp, TileOf(ground.Value, worldTile), Tile.Empty);

        if (movedSteps is { } dir)
            PlaceSteps(ground.Value, floor.Value, worldTile, dir);
        else if (fresh && from is { } digger && StepsDirection(worldTile, digger) is { } toDigger)
            PlaceSteps(ground.Value, floor.Value, worldTile, toDigger);

        UpdateRims(map, worldTile);
        return true;
    }

    private static readonly Vector2i[] Cardinals = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];

    /// <summary>
    /// The side of <paramref name="worldTile"/> the digger stands on, along the longer axis
    /// </summary>
    private Vector2i? StepsDirection(Vector2i worldTile, EntityCoordinates digger)
    {
        var delta = _transform.ToMapCoordinates(digger).Position - (worldTile + new Vector2(0.5f));
        if (delta.LengthSquared() < 0.01f)
            return null;

        return MathF.Abs(delta.X) >= MathF.Abs(delta.Y)
            ? new Vector2i(MathF.Sign(delta.X), 0)
            : new Vector2i(0, MathF.Sign(delta.Y));
    }

    /// <summary>
    /// Steps on <paramref name="worldTile"/> of the trench floor climbing towards <paramref name="dir"/>, if the
    /// ground they lead up to is still there
    /// </summary>
    private void PlaceSteps(Entity<MapGridComponent> ground, Entity<MapGridComponent> floor, Vector2i worldTile, Vector2i dir)
    {
        var exit = TileOf(ground, worldTile + dir);
        if (!_map.TryGetTileRef(ground, ground.Comp, exit, out var exitRef) || exitRef.Tile.IsEmpty)
            return;

        // a ladder rotated to face south climbs north: it faces away from where it leads
        var facing = (-new Vector2(dir.X, dir.Y)).GetDir();
        var steps = Spawn(TrenchSteps, _map.GridTileToLocal(floor, floor.Comp, TileOf(floor, worldTile)));
        _transform.SetLocalRotation(steps, facing.ToAngle());
    }

    /// <summary>
    /// Removes the steps of the trench next to <paramref name="worldTile"/> that lead up onto it and says which way
    /// they climbed
    /// </summary>
    private Vector2i? TakeStepsLeadingTo(Entity<MapGridComponent> floor, Vector2i worldTile)
    {
        foreach (var side in Cardinals)
        {
            var anchored = _map.GetAnchoredEntitiesEnumerator(floor, floor.Comp, TileOf(floor, worldTile - side));
            while (anchored.MoveNext(out var uid))
            {
                if (MetaData(uid.Value).EntityPrototype?.ID != TrenchSteps.Id)
                    continue;

                // the way these steps climb, see PlaceSteps
                var climbs = Transform(uid.Value).LocalRotation.GetCardinalDir().GetOpposite().ToIntVec();
                if (climbs != side)
                    continue;

                Del(uid.Value);
                return side;
            }
        }

        return null;
    }

    private Vector2i TileOf(Entity<MapGridComponent> grid, Vector2i worldTile)
    {
        return _map.WorldToTile(grid, grid.Comp, worldTile + new Vector2(0.5f));
    }

    private bool HasWall(Entity<MapGridComponent> grid, Vector2i index)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID == TrenchWall.Id)
                return true;
        }

        return false;
    }

    private void RemoveWalls(Entity<MapGridComponent> grid, Vector2i index)
    {
        var walls = new List<EntityUid>();
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID == TrenchWall.Id)
                walls.Add(uid.Value);
        }

        foreach (var wall in walls)
        {
            Del(wall);
        }
    }

    [SubscribeLocalEvent]
    private void OnRoofPlacerInit(Entity<ST14RoofPlacerComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (xform.MapUid is { } map && TryComp<ST14ZMapComponent>(map, out var zMap) &&
            _zLevels.TryMapUp((map, zMap), out var above))
        {
            var world = _transform.GetWorldPosition(xform);
            var center = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
            var tiles = new List<Vector2i>();
            for (var dx = -ent.Comp.Radius; dx <= ent.Comp.Radius; dx++)
            {
                for (var dy = -ent.Comp.Radius; dy <= ent.Comp.Radius; dy++)
                {
                    tiles.Add(center + new Vector2i(dx, dy));
                }
            }

            PlaceRoofs(above, tiles, ent.Comp.Roof);
        }

        QueueDel(ent);
    }

    /// <summary>
    /// Puts a roof on every open tile of <paramref name="level"/> among <paramref name="worldTiles"/>: over a dug-out
    /// on the surface, over a bunker on the level above the ground. Ground and roofs already there are left alone
    /// </summary>
    public int PlaceRoofs(EntityUid level, IEnumerable<Vector2i> worldTiles, EntProtoId? roofProto = null)
    {
        if (!_zLevels.TryGetLayerGrid(level, out var roof))
            return 0;

        var placed = 0;
        var mapId = Transform(level).MapID;
        foreach (var worldTile in worldTiles)
        {
            var index = TileOf(roof.Value, worldTile);
            if (_map.TryGetTileRef(roof.Value, roof.Value.Comp, index, out var tileRef) && !tileRef.Tile.IsEmpty ||
                _roofs.TryGetRoofEntity(level, worldTile, out _))
            {
                continue;
            }

            // on the surface the roof of a dug-out sticks up out of the ground; up on +1 a bunker roof is walked on
            var proto = roofProto ?? (TryComp<ST14ZMapComponent>(level, out var zMap) && zMap.Depth == 0 ? DugoutRoof : BunkerRoof);
            Spawn(proto, new MapCoordinates(worldTile + new Vector2(0.5f), mapId));
            placed++;
        }

        RefreshRoofOutlines(level, worldTiles);

        return placed;
    }
}

/// <summary>
/// Builds a sample line of trenches with a machine-gun bunker and a command dug-out east of the admin, on the
/// z-levels of the sector they stand on.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ST14SpawnTrenchTestCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "st14-spawn-trench-test";
    public override string Description => "Digs a sample trench line with a machine-gun bunker and a command dug-out east of you";
    public override string Help => "st14-spawn-trench-test";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError("You need a body on a sector map");
            return;
        }

        var trench = _entities.System<ST14TrenchSystem>();
        var transform = _entities.System<TransformSystem>();
        var zLevels = _entities.System<ST14ZLevelsSystem>();

        var xform = _entities.GetComponent<TransformComponent>(player);
        if (xform.MapUid is not { } map || !_entities.TryGetComponent<ST14ZMapComponent>(map, out var zMap) ||
            !zLevels.TryMapDown((map, zMap), out var below))
        {
            shell.WriteError("This map has no level below; give it layers with znetwork-layers first");
            return;
        }

        var world = transform.GetWorldPosition(xform);
        var o = new Vector2i((int) MathF.Floor(world.X) + 3, (int) MathF.Floor(world.Y));

        // a firing trench ten tiles long, running east, with a branch north to the command dug-out
        for (var x = 0; x < 10; x++)
        {
            trench.DigTrench(map, o + new Vector2i(x, 0));
        }

        for (var y = 1; y <= 2; y++)
        {
            trench.DigTrench(map, o + new Vector2i(8, y));
        }

        // the command dug-out: a 3x3 pit at the end of the branch, roofed with concrete on the level above it
        for (var x = 7; x <= 9; x++)
        {
            for (var y = 3; y <= 5; y++)
            {
                trench.DigTrench(map, o + new Vector2i(x, y));
            }
        }

        _entities.SpawnEntity("ST14BunkerRoofFrame", Center(below, o + new Vector2i(8, 4)));

        // steps out of the trench at both ends (they climb north)
        _entities.SpawnEntity("ST14TrenchSteps", Center(below, o));
        _entities.SpawnEntity("ST14TrenchSteps", Center(below, o + new Vector2i(9, 0)));

        // the machine-gun bunker on the surface south of the trench: concrete walls, slits facing south, the way in
        // from the trench side, a concrete roof on the level above
        var b = o + new Vector2i(3, -4);
        for (var x = 0; x <= 4; x++)
        {
            for (var y = 0; y <= 4; y++)
            {
                var edge = x == 0 || y == 0 || x == 4 || y == 4;
                if (!edge)
                    continue;

                // the door, facing the trench
                if (y == 4 && x == 2)
                    continue;

                var slit = y == 0 && x is 1 or 2 or 3;
                _entities.SpawnEntity(slit ? "ST14BunkerSlit" : "ST14BunkerWall", Center(map, b + new Vector2i(x, y)));
            }
        }

        _entities.SpawnEntity("ST14BunkerRoofFrame", Center(map, b + new Vector2i(2, 2)));

        shell.WriteLine($"Trench line dug from {o} east, machine-gun bunker at {b}, command dug-out at {o + new Vector2i(8, 4)} on the level below");
    }

    private static MapCoordinates Center(EntityUid map, Vector2i worldTile)
    {
        return new MapCoordinates(worldTile + new Vector2(0.5f), IoCManager.Resolve<IEntityManager>().GetComponent<MapComponent>(map).MapId);
    }
}
