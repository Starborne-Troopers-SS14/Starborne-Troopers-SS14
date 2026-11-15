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
/// only walk along. Steps (a ladder) lead out. A soldier in the trench is a level lower: the ground of the level above
/// stands between them and the fire on the surface, and the trench is open to the sky only where it was dug.
/// </summary>
public sealed partial class ST14TrenchSystem : EntitySystem
{
    public static readonly EntProtoId TrenchWall = "ST14TrenchWall";
    public static readonly ProtoId<ContentTileDefinition> TrenchFloor = "FloorDirt";

    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<ST14TrenchDiggerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null)
            return;

        if (!TryGetDigSpot(args.ClickLocation, out _, out var worldTile, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
            return;
        }

        args.Handled = true;
        var location = new EntityCoordinates(_transform.GetMap(args.ClickLocation)!.Value, worldTile + new Vector2(0.5f));
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.DigTime,
            new ST14DigTrenchDoAfterEvent(GetNetCoordinates(location)), ent, used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnDigDone(Entity<ST14TrenchDiggerComponent> ent, ref ST14DigTrenchDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var coords = GetCoordinates(args.Location);
        if (!TryGetDigSpot(coords, out var map, out var worldTile, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
            return;
        }

        DigTrench(map, worldTile);
        _popup.PopupEntity(Loc.GetString("st14-trench-dug"), args.User, args.User);
    }

    /// <summary>
    /// Where a dig at <paramref name="coords"/> lands: a tile of ground on a level that has a level below it
    /// </summary>
    private bool TryGetDigSpot(EntityCoordinates coords, out EntityUid map, out Vector2i worldTile, out string reason)
    {
        worldTile = default;
        reason = "st14-trench-nothing-below";
        map = _transform.GetMap(coords) ?? EntityUid.Invalid;

        if (!TryComp<ST14ZMapComponent>(map, out var zMap) || !_zLevels.TryMapDown((map, zMap), out _))
            return false;

        var world = _transform.ToMapCoordinates(coords).Position;
        worldTile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));

        if (!_zLevels.TryGetLayerGrid(map, out var grid) ||
            !_map.TryGetTileRef(grid.Value, grid.Value.Comp, _map.WorldToTile(grid.Value, grid.Value.Comp, world), out var tile) ||
            tile.Tile.IsEmpty)
        {
            reason = "st14-trench-already-dug";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Opens a world tile of a level into the level below and makes it a trench there. Untouched neighbours of the
    /// trench get earth walls on the level below; a wall in the way of the trench goes
    /// </summary>
    public bool DigTrench(EntityUid map, Vector2i worldTile)
    {
        if (!TryComp<ST14ZMapComponent>(map, out var zMap) ||
            !_zLevels.TryMapDown((map, zMap), out var below) ||
            !_zLevels.TryGetLayerGrid(map, out var ground) ||
            !_zLevels.TryGetLayerGrid(below, out var floor))
        {
            return false;
        }

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
        return true;
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
        if (xform.MapUid is not { } map || !TryComp<ST14ZMapComponent>(map, out var zMap) ||
            !_zLevels.TryMapUp((map, zMap), out var above) || !_zLevels.TryGetLayerGrid(above, out var roof))
        {
            QueueDel(ent);
            return;
        }

        var world = _transform.GetWorldPosition(xform);
        var center = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        var tile = new Tile(_tiles[ent.Comp.Tile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var dx = -ent.Comp.Radius; dx <= ent.Comp.Radius; dx++)
        {
            for (var dy = -ent.Comp.Radius; dy <= ent.Comp.Radius; dy++)
            {
                tiles.Add((TileOf(roof.Value, center + new Vector2i(dx, dy)), tile));
            }
        }

        _map.SetTiles(roof.Value, roof.Value.Comp, tiles);
        QueueDel(ent);
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
