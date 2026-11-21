/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Shared.Physics;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Digging with the entrenching tool, the Foxhole way. The soldier lays out a segment on the ground (a straight
/// trench or a square dug-out; it runs away from them) and digs it bit by bit, seeing how much is done; when it is done
/// the whole segment opens into the level below at once and a dug-out gets its roof. Standing in a trench they can lay
/// out the next segment on the bank and dig it from inside. Steps are cut into a trench as an upgrade; without them a
/// soldier climbs out over the side, slowly.
/// </summary>
public sealed partial class ST14TrenchSystem
{
    public static readonly EntProtoId Blueprint = "ST14TrenchBlueprint";
    public static readonly EntProtoId BlueprintMarker = "ST14TrenchBlueprintMarker";

    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<ST14TrenchDiggerComponent> ent, ref AfterInteractEvent args)
    {
        // in cable mode the cable trench system digs (Content.Server/_ST14/EnergyFence/ST14TrenchSystem.cs)
        if (args.Handled || ent.Comp.CableMode)
            return;

        // the spade works on the ground where it is pointed, whatever stands there: a trooper on the layout (the one
        // digging too), a dropped item, a wall. It used to give up on any of them, so parts of a layout would not dig
        args.Handled = true;
        UseAt(ent, args.User, args.ClickLocation);
    }

    private void StartSwing(Entity<ST14TrenchDiggerComponent> digger, EntityUid user, EntityUid blueprint)
    {
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, digger.Comp.SwingTime,
            new ST14DigBlueprintDoAfterEvent(), digger, target: blueprint, used: digger)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    /// <summary>
    /// How far from the soldier a segment can be laid out: they walk up to it to dig
    /// </summary>
    private const float PlaceRange = 6f;

    /// <summary>
    /// How close to a segment the soldier must be to start digging it straight away
    /// </summary>
    private const float DigReach = 1.75f;

    [Dependency] private Content.Shared.Hands.EntitySystems.SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14SetDigModeEvent>(OnSetMode);
        InitializeClimb();
    }

    private bool TryGetHeldDigger(EntityUid? user, NetEntity netTool, out Entity<ST14TrenchDiggerComponent> digger)
    {
        digger = default;
        if (user is not { } holder || !TryGetEntity(netTool, out var tool) ||
            !TryComp<ST14TrenchDiggerComponent>(tool, out var comp) || !_hands.IsHolding(holder, tool.Value))
        {
            return false;
        }

        digger = (tool.Value, comp);
        return true;
    }

    private void OnSetMode(ST14SetDigModeEvent ev, EntitySessionEventArgs args)
    {
        if (!TryGetHeldDigger(args.SenderSession.AttachedEntity, ev.Tool, out var digger) || !Enum.IsDefined(ev.Mode) ||
            ev.Direction is not (Direction.North or Direction.East or Direction.South or Direction.West))
        {
            return;
        }

        var length = Math.Clamp(ev.Length, 1, ST14TrenchDiggerComponent.MaxTrenchLength);
        var width = Math.Clamp(ev.Width, 1, ST14TrenchDiggerComponent.MaxTrenchWidth);
        if (digger.Comp.Mode == ev.Mode && digger.Comp.Direction == ev.Direction &&
            digger.Comp.TrenchLength == length && digger.Comp.TrenchWidth == width)
        {
            return;
        }

        digger.Comp.Mode = ev.Mode;
        digger.Comp.Direction = ev.Direction;
        digger.Comp.TrenchLength = length;
        digger.Comp.TrenchWidth = width;
        Dirty(digger);
    }

    /// <summary>
    /// The entrenching tool used on <paramref name="coords"/> in its mode and turned its way (both set in the build
    /// window): lays out a segment there or goes on digging one, or cuts or turns steps
    /// </summary>
    public void UseAt(Entity<ST14TrenchDiggerComponent> digger, EntityUid user, EntityCoordinates coords)
    {
        var mode = digger.Comp.Mode;
        if (mode is not (ST14DigMode.Trench or ST14DigMode.Dugout or ST14DigMode.Corner or ST14DigMode.Door or
            ST14DigMode.Steps or ST14DigMode.Fill))
        {
            return;
        }

        var userPos = _transform.GetWorldPosition(user);
        if (!coords.IsValid(EntityManager) ||
            (_transform.ToMapCoordinates(coords).Position - userPos).Length() > PlaceRange)
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-too-far"), user, user);
            return;
        }

        if (!TryGetGround(coords, out var ground, out var worldTile, out var dug))
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-nothing-below"), user, user);
            return;
        }

        if (mode == ST14DigMode.Door)
        {
            CutDoor(digger, user, ground, _transform.ToMapCoordinates(coords).Position, userPos);
            return;
        }

        if (mode == ST14DigMode.Fill)
        {
            if (!dug)
                _popup.PopupEntity(Loc.GetString("st14-trench-not-dug"), user, user);
            else if (!NearTiles(userPos, [worldTile]))
                _popup.PopupEntity(Loc.GetString("st14-trench-too-far"), user, user);
            else
                StartFill(digger, user, ground, worldTile);

            return;
        }

        if (mode == ST14DigMode.Steps)
        {
            if (!dug)
            {
                _popup.PopupEntity(Loc.GetString("st14-trench-not-dug"), user, user);
                return;
            }

            if (!NearTiles(userPos, [worldTile]))
            {
                _popup.PopupEntity(Loc.GetString("st14-trench-too-far"), user, user);
                return;
            }

            var location = new EntityCoordinates(ground, worldTile + new Vector2(0.5f));
            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, digger.Comp.UpgradeTime,
                new ST14TrenchStepsDoAfterEvent(GetNetCoordinates(location), digger.Comp.Direction), digger, used: digger)
            {
                BreakOnMove = true,
                NeedHand = true,
            });
            return;
        }

        // a click on a segment already laid out goes on digging it
        var blueprint = BlueprintAt(ground, worldTile);
        if (blueprint == null)
        {
            var world = _transform.ToMapCoordinates(coords).Position;
            var shape = mode is ST14DigMode.Dugout or ST14DigMode.Corner
                // a bunker goes flush against the bunkers next to it, as the ghost showed
                ? ST14TrenchShapes.BunkerShape(mode,
                    ST14TrenchShapes.SnappedBunkerStart(world, digger.Comp.DugoutSize, t => DugoutAt(ground, t)),
                    digger.Comp.Direction, digger.Comp.DugoutSize)
                : ST14TrenchShapes.Shape(mode, world, digger.Comp.Direction,
                    digger.Comp.TrenchLength, digger.Comp.DugoutSize, digger.Comp.TrenchWidth);
            blueprint = TryLayOut(ground, shape, mode is ST14DigMode.Dugout or ST14DigMode.Corner, (float) digger.Comp.DigTime.TotalSeconds);
        }

        if (blueprint is not { } segment)
        {
            _popup.PopupEntity(Loc.GetString(dug ? "st14-trench-already-dug" : "st14-trench-bad-spot"), user, user);
            return;
        }

        if (NearTiles(userPos, Comp<ST14TrenchBlueprintComponent>(segment).Tiles))
            StartSwing(digger, user, segment);
        else
            _popup.PopupEntity(Loc.GetString("st14-trench-laid-out"), user, user);
    }

    private static bool NearTiles(Vector2 position, IEnumerable<Vector2i> tiles)
    {
        foreach (var tile in tiles)
        {
            var closest = Vector2.Clamp(position, tile, tile + Vector2.One);
            if ((closest - position).Length() <= DigReach)
                return true;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnSwing(Entity<ST14TrenchDiggerComponent> ent, ref ST14DigBlueprintDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target ||
            !TryComp<ST14TrenchBlueprintComponent>(target, out var blueprint))
        {
            return;
        }

        args.Handled = true;
        blueprint.Progress += (float) ent.Comp.SwingTime.TotalSeconds;
        if (blueprint.Progress >= blueprint.Total)
        {
            FinishBlueprint((target, blueprint));
            _popup.PopupEntity(Loc.GetString("st14-trench-dug"), args.User, args.User);
            return;
        }

        var percent = (int) MathF.Floor(blueprint.Progress / blueprint.Total * 100f);
        _popup.PopupEntity(Loc.GetString("st14-trench-progress", ("percent", percent)), args.User, args.User);
        args.Repeat = true;
    }

    /// <summary>
    /// Digs out the whole segment at once and roofs it if it is a dug-out
    /// </summary>
    public void FinishBlueprint(Entity<ST14TrenchBlueprintComponent> blueprint)
    {
        var tiles = new List<Vector2i>(blueprint.Comp.Tiles);
        var roofed = blueprint.Comp.Roofed;
        var door = blueprint.Comp.Door;
        var map = Transform(blueprint).MapUid;
        Del(blueprint);

        if (map is not { } ground)
            return;

        foreach (var tile in tiles)
        {
            DigTrench(ground, tile);
        }

        if (roofed)
        {
            PlaceRoofs(ground, tiles);
            MarkDugout(ground, tiles);
        }
        else if (!door)
        {
            // a trench dug up to a dug-out does not open it: the wall stays until a door is cut
            CloseOffDugouts(ground, tiles);
        }

        // layouts next to what was just dug join it
        RefreshBlueprintOutlines(ground);
    }

    [SubscribeLocalEvent]
    private void OnBlueprintShutdown(Entity<ST14TrenchBlueprintComponent> ent, ref ComponentShutdown args)
    {
        foreach (var marker in ent.Comp.Markers)
        {
            QueueDel(marker);
        }

        // the layouts next to it close their string where it was. Its own transform may already be gone, its stakes
        // still stand on the map
        foreach (var marker in ent.Comp.Markers)
        {
            if (TryComp(marker, out TransformComponent? markerXform) && markerXform.MapUid is { } map && !TerminatingOrDeleted(map))
            {
                RefreshBlueprintOutlines(map, ent);
                break;
            }
        }
    }

    /// <summary>
    /// The ground a click is about: the surface (depth 0, a level with one below it) under the click, or, for a click
    /// from inside a trench, the surface above it. <paramref name="dug"/> is whether that tile of the surface is open
    /// </summary>
    private bool TryGetGround(EntityCoordinates coords, out EntityUid ground, out Vector2i worldTile, out bool dug)
    {
        ground = default;
        worldTile = default;
        dug = false;

        if (_transform.GetMap(coords) is not { } map || !TryComp<ST14ZMapComponent>(map, out var zMap))
            return false;

        if (zMap.Depth == 0)
            ground = map;
        else if (zMap.Depth == -1 && _zLevels.TryMapUp((map, zMap), out var above))
            ground = above;
        else
            return false;

        if (!_zLevels.TryMapDown(ground, out _) || !_zLevels.TryGetLayerGrid(ground, out var grid))
            return false;

        var world = _transform.ToMapCoordinates(coords).Position;
        worldTile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        dug = !IsGround(grid.Value, worldTile);
        return true;
    }

    /// <summary>
    /// Something solid stands on the tile: a wall, a rock, a bunker
    /// </summary>
    private bool IsBlocked(Entity<MapGridComponent> grid, Vector2i worldTile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, TileOf(grid, worldTile));
        while (anchored.MoveNext(out var uid))
        {
            if (!_fixturesQuery.TryComp(uid.Value, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                    return true;
            }
        }

        return false;
    }

    private bool IsGround(Entity<MapGridComponent> grid, Vector2i worldTile)
    {
        return _map.TryGetTileRef(grid, grid.Comp, TileOf(grid, worldTile), out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    /// <summary>
    /// The segment laid out on <paramref name="worldTile"/> of <paramref name="ground"/>
    /// </summary>
    public EntityUid? BlueprintAt(EntityUid ground, Vector2i worldTile)
    {
        var query = EntityQueryEnumerator<ST14TrenchBlueprintComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var blueprint, out var xform))
        {
            if (xform.MapUid == ground && blueprint.Tiles.Contains(worldTile))
                return uid;
        }

        return null;
    }

    /// <summary>
    /// Lays out a segment over <paramref name="shape"/> (world tiles of the surface <paramref name="ground"/>)
    /// </summary>
    public EntityUid? TryLayOut(EntityUid ground, IReadOnlyList<Vector2i> shape, bool roofed, float secondsPerTile)
    {
        if (!_zLevels.TryGetLayerGrid(ground, out var grid))
            return null;

        var tiles = new List<Vector2i>();
        foreach (var tile in shape)
        {
            if (BlueprintAt(ground, tile) != null)
                return null;

            // dug already: a trench joins the trench there; a dug-out is dug whole into untouched ground
            if (!IsGround(grid.Value, tile))
            {
                if (roofed)
                    return null;

                continue;
            }

            if (IsBlocked(grid.Value, tile))
                return null;

            tiles.Add(tile);
        }

        if (tiles.Count == 0 || !DugoutWallsHold(ground, grid.Value, tiles, roofed))
            return null;

        var mapId = Transform(ground).MapID;
        var uid = Spawn(Blueprint, new MapCoordinates(tiles[0] + new Vector2(0.5f), mapId));
        var blueprint = EnsureComp<ST14TrenchBlueprintComponent>(uid);
        blueprint.Tiles = tiles;
        blueprint.Roofed = roofed;
        blueprint.Total = MathF.Max(0.1f, secondsPerTile * tiles.Count);
        foreach (var tile in tiles)
        {
            blueprint.Markers.Add(Spawn(BlueprintMarker, new MapCoordinates(tile + new Vector2(0.5f), mapId)));
        }

        RefreshBlueprintOutlines(ground);

        return uid;
    }

    [SubscribeLocalEvent]
    private void OnStepsDone(Entity<ST14TrenchDiggerComponent> ent, ref ST14TrenchStepsDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!TryGetGround(GetCoordinates(args.Location), out var ground, out var worldTile, out var dug) || !dug)
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-not-dug"), args.User, args.User);
            return;
        }

        Vector2i? climb = args.Climb == Direction.Invalid ? null : args.Climb.ToIntVec();
        var result = ToggleSteps(ground, worldTile, climb);
        _popup.PopupEntity(Loc.GetString(result switch
        {
            true => "st14-trench-steps-cut",
            false => "st14-trench-steps-filled",
            null => "st14-trench-steps-nowhere",
        }), args.User, args.User);
    }

    /// <summary>
    /// Cuts steps out of the trench under <paramref name="worldTile"/> of the surface, or fills in the steps already
    /// there. Without <paramref name="climb"/> they climb out over the end of a trench, elsewhere over a side. With it
    /// they climb that way, and steps already there that climb another way are turned instead of filled in.
    /// </summary>
    /// <returns>true when cut or turned, false when filled in, null when there is no trench or no ground to climb to</returns>
    public bool? ToggleSteps(EntityUid ground, Vector2i worldTile, Vector2i? climb = null)
    {
        if (!_zLevels.TryMapDown(ground, out var below) ||
            !_zLevels.TryGetLayerGrid(ground, out var surface) ||
            !_zLevels.TryGetLayerGrid(below, out var floor))
        {
            return null;
        }

        // not dug: the level below is solid earth under it
        if (IsGround(surface.Value, worldTile))
            return null;

        var index = TileOf(floor.Value, worldTile);
        if (!_map.TryGetTileRef(floor.Value, floor.Value.Comp, index, out var floorRef) || floorRef.Tile.IsEmpty)
            return null;

        var old = new List<EntityUid>();
        Vector2i? oldClimb = null;
        var anchored = _map.GetAnchoredEntitiesEnumerator(floor.Value, floor.Value.Comp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID != TrenchSteps.Id)
                continue;

            old.Add(uid.Value);
            // the way these steps climb, see PlaceSteps
            oldClimb = Transform(uid.Value).LocalRotation.GetCardinalDir().GetOpposite().ToIntVec();
        }

        // the same steps again: filled in
        if (old.Count > 0 && (climb == null || climb == oldClimb))
        {
            foreach (var uid in old)
            {
                Del(uid);
            }

            return false;
        }

        var dir = climb ?? EndOfTrench(surface.Value, worldTile);
        if (dir is not { } way || !IsGround(surface.Value, worldTile + way))
            return null;

        foreach (var uid in old)
        {
            Del(uid);
        }

        PlaceSteps(surface.Value, floor.Value, worldTile, way);
        return true;
    }

    /// <summary>
    /// The way out of a trench tile: over its end if it is one, else over the first side with ground
    /// </summary>
    private Vector2i? EndOfTrench(Entity<MapGridComponent> surface, Vector2i worldTile)
    {
        Vector2i? best = null;
        foreach (var side in Cardinals)
        {
            if (!IsGround(surface, worldTile + side))
                continue;

            best ??= side;

            // the trench runs on behind: this is its end, the steps climb out straight ahead
            if (!IsGround(surface, worldTile - side))
                return side;
        }

        return best;
    }

    [SubscribeLocalEvent]
    private void OnClimbVerbs(Entity<ST14ClimbUpComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || Transform(args.User).MapUid != Transform(ent).MapUid)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.Verb),
            Priority = 2,
            Act = () => StartClimb(ent, user),
        });
    }

    private void StartClimb(Entity<ST14ClimbUpComponent> ent, EntityUid user)
    {
        if (!TryGetClimbSpot(ent, out _))
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-climb-blocked"), user, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.ClimbTime,
            new ST14ClimbOutOfTrenchDoAfterEvent(), ent, target: ent)
        {
            BreakOnMove = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnClimbDone(Entity<ST14ClimbUpComponent> ent, ref ST14ClimbOutOfTrenchDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!TryGetClimbSpot(ent, out var spot))
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-climb-blocked"), args.User, args.User);
            return;
        }

        _transform.SetWorldPosition(args.User, spot);
        _zLevels.TryMove(args.User, 1);
    }

    /// <summary>
    /// Where a soldier stands after climbing up over <paramref name="wall"/>: on the ground of the level above it (out of a
    /// trench), or on top of the wall itself if it is high ground (onto a bunker roof)
    /// </summary>
    public bool TryGetClimbSpot(EntityUid wall, out Vector2 spot)
    {
        var xform = Transform(wall);
        spot = _transform.GetWorldPosition(xform);
        if (xform.MapUid is not { } map || !_zLevels.TryMapUp(map, out var above) ||
            !_zLevels.TryGetLayerGrid(above, out var grid))
        {
            return false;
        }

        var tile = new Vector2i((int) MathF.Floor(spot.X), (int) MathF.Floor(spot.Y));
        if (IsBlocked(grid.Value, tile))
            return false;

        return IsGround(grid.Value, tile) || HasComp<ST14ZLevelHighGroundComponent>(wall) || HasComp<ST14BunkerRoofComponent>(wall);
    }
}
