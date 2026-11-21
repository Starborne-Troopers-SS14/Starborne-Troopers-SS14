/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Robust.Shared.Analyzers;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Inventory;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._ST14.ZLevels.Core.EntitySystems;

public abstract partial class ST14SharedZLevelsSystem
{
    private TimeSpan _accumulatedTime = TimeSpan.Zero;
    private readonly List<EntityUid> _dirtyMovementBodies = new();

    // ST14: O(1) membership mirror of _dirtyMovementBodies (MoveEvent fires for every step of every body).
    private readonly HashSet<EntityUid> _dirtyMovementSet = new();

    // ST14: bodies found by a tile-change or high-ground invalidation, deduplicated, insertion ordered.
    private readonly HashSet<Entity<ST14ZPhysicsComponent>> _tileChangeBodies = new();
    private readonly HashSet<Vector2i> _changedTiles = new();

    /// <summary>
    /// ST14: above this many changed tiles in one event, one lookup over the bounding box of all of them
    /// is cheaper than one lookup per tile (map loading, explosions, big construction).
    /// </summary>
    private const int BulkTileChangeThreshold = 32;

    /// <summary>
    /// ST14: bumped whenever something that the per-tile ground cache can't track precisely changes
    /// (z-network layout, tiles of grids that are not z-maps). Bodies with an older epoch recompute.
    /// </summary>
    private int _groundCacheEpoch;

    /// <summary>
    /// ST14: incremented on every tile change of any grid. Lets callers cache tile scans.
    /// </summary>
    public int TileVersion { get; private set; }

    /// <summary>
    /// Returns the last cached distance to the floor.
    /// </summary>
    /// <param name="target">The entity, the distance to the floor which we calculate</param>
    /// <returns></returns>
    [PublicAPI]
    public float DistanceToGround(Entity<ST14ZPhysicsComponent?> target)
    {
        if (!Resolve(target, ref target.Comp, false))
            return 0;

        return target.Comp.LocalPosition - target.Comp.CachedGroundHeight;
    }

    [SubscribeLocalEvent]
    private void OnTileChanged(Entity<ST14ZMapComponent> ent, ref TileChangedEvent args)
    {
        if (_gridQuery.TryComp(args.Entity, out var grid))
            RefreshBodiesOnTileChange(args.Entity, grid, ent, args.Changes);
    }

    /// <summary>
    /// ST14: the floor of a level changed under the bodies on it. Their ground is recomputed and they wake,
    /// so a body standing on a tile that was dug out falls at once. Bodies on the level above standing over an
    /// opening have their ground on this level too, so they are refreshed as well (the levels share world
    /// coordinates).
    /// </summary>
    private void RefreshBodiesOnTileChange(EntityUid gridUid, MapGridComponent grid, Entity<ST14ZMapComponent> map, ReadOnlySpan<TileChangedEntry> changes)
    {
        var mapId = _transformQuery.GetComponent(map).MapID;
        var bodies = _tileChangeBodies;
        bodies.Clear();

        CollectBodiesOnChangedTiles(gridUid, grid, mapId, changes);

        if (TryMapOffset(map.AsNullable(), 1, out _, out var aboveMap))
            CollectBodiesOnChangedTiles(gridUid, grid, aboveMap.MapId, changes);

        foreach (var body in bodies)
        {
            if (TerminatingOrDeleted(body))
                continue;

            RequestCacheMovement(body);
            RefreshBody(body);
        }

        bodies.Clear();
    }

    /// <summary>
    /// ST14: a grid that is not the map itself. When it stands on a z-map (a sector's main level is a map with its
    /// ground as a separate grid) it gets the same targeted refresh; any other grid only invalidates every
    /// cached ground, there is no per-tile bookkeeping for it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAnyTileChanged(ref TileChangedEvent args)
    {
        TileVersion++; // ST14: lets the client cache tile scans (see ST14ClientZLevelsSystem)

        if (_zMapQuery.HasComp(args.Entity))
            return;

        if (_gridQuery.TryComp(args.Entity, out var grid) &&
            _transformQuery.TryComp(args.Entity, out var xform) &&
            xform.MapUid is { } mapUid &&
            _zMapQuery.TryComp(mapUid, out var zMap))
        {
            RefreshBodiesOnTileChange(args.Entity, grid, (mapUid, zMap), args.Changes);
            return;
        }

        _groundCacheEpoch++;
    }


    private void CollectBodiesOnChangedTiles(EntityUid gridUid, MapGridComponent grid, MapId lookupMap, ReadOnlySpan<TileChangedEntry> changes)
    {
        var half = grid.TileSizeHalfVector;

        if (changes.Length <= BulkTileChangeThreshold)
        {
            foreach (var change in changes)
            {
                var center = _map.GridTileToWorldPos(gridUid, grid, change.GridIndices);
                _lookup.GetEntitiesIntersecting(lookupMap, new Box2(center - half, center + half), _tileChangeBodies, LookupFlags.Uncontained);
            }

            return;
        }

        // Bulk: one lookup over the bounding box, then keep the bodies whose tile actually changed.
        _changedTiles.Clear();
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var change in changes)
        {
            _changedTiles.Add(change.GridIndices);
            var center = _map.GridTileToWorldPos(gridUid, grid, change.GridIndices);
            min = Vector2.Min(min, center - half);
            max = Vector2.Max(max, center + half);
        }

        _bulkLookup.Clear();
        _lookup.GetEntitiesIntersecting(lookupMap, new Box2(min, max), _bulkLookup, LookupFlags.Uncontained);
        foreach (var body in _bulkLookup)
        {
            var worldPos = _transform.GetWorldPosition(body.Owner);
            if (_changedTiles.Contains(_map.WorldToTile(gridUid, grid, worldPos)))
                _tileChangeBodies.Add(body);
        }

        _bulkLookup.Clear();
        _changedTiles.Clear();
    }

    private readonly HashSet<Entity<ST14ZPhysicsComponent>> _bulkLookup = new();

    /// <summary>
    /// ST14: stairs and other high ground were placed, removed, anchored or turned. Bodies standing on that
    /// tile (and above it, over an opening) may have cached a flat floor there, so recompute them.
    /// </summary>
    private void InvalidateGroundAt(EntityUid source)
    {
        if (!_transformQuery.TryComp(source, out var xform) || xform.MapUid is not { } mapUid)
            return;

        var center = _transform.GetWorldPosition(xform);
        var box = new Box2(center - new Vector2(0.5f), center + new Vector2(0.5f));

        var bodies = _tileChangeBodies;
        bodies.Clear();
        _lookup.GetEntitiesIntersecting(xform.MapID, box, bodies, LookupFlags.Uncontained);
        if (_zMapQuery.TryComp(mapUid, out var zMap) && TryMapOffset((mapUid, zMap), 1, out _, out var aboveMap))
            _lookup.GetEntitiesIntersecting(aboveMap.MapId, box, bodies, LookupFlags.Uncontained);

        foreach (var body in bodies)
        {
            if (body.Owner != source && !TerminatingOrDeleted(body))
                RequestCacheMovement(body);
        }

        bodies.Clear();
    }

    /// <summary>
    /// ST14: the z-network layout changed (levels added, removed or relinked), so "is there a map below"
    /// answers in cached ground heights may be stale. Called by the server when it edits a network.
    /// </summary>
    public void InvalidateGroundCache()
    {
        _groundCacheEpoch++;
    }

    /// <summary>
    /// ST14: world tiles of a level that are walkable ground although the level has no tile there (bunker roofs:
    /// whoever climbs onto one walks on it and does not fall through). Set by the roof system.
    /// </summary>
    private readonly HashSet<(EntityUid Map, Vector2i Tile)> _solidSpots = new();

    public void SetSolidSpot(EntityUid map, Vector2i worldTile, bool solid)
    {
        if (solid ? _solidSpots.Add((map, worldTile)) : _solidSpots.Remove((map, worldTile)))
            _groundCacheEpoch++;
    }

    [Dependency] private EntityQuery<Robust.Shared.Physics.FixturesComponent> _fixturesQuery = default!;

    /// <summary>
    /// How much a body may overhang the edge of an opening and still fall through it
    /// </summary>
    private const float FitTolerance = 0.05f;

    /// <summary>
    /// ST14: whether the body fits through the opening it stands over on <paramref name="level"/>: every tile its solid
    /// outline covers there is open (no floor, no roof). A body wider than a trench does not drop into it, nor can it be
    /// let down there: it stays on the edge.
    /// </summary>
    public bool FitsThrough(EntityUid uid, EntityUid level, Vector2 worldPos)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return true;

        var transform = new Robust.Shared.Physics.Transform(worldPos, _transform.GetWorldRotation(uid));
        Box2? outline = null;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var box = fixture.Shape.ComputeAABB(transform, i);
                outline = outline?.Union(box) ?? box;
            }
        }

        if (outline is not { } body)
            return true;

        body = body.Enlarged(-FitTolerance);
        if (body.Width <= 0f || body.Height <= 0f)
            return true;

        for (var x = (int) MathF.Floor(body.Left); x <= (int) MathF.Floor(body.Right); x++)
        {
            for (var y = (int) MathF.Floor(body.Bottom); y <= (int) MathF.Floor(body.Top); y++)
            {
                if (_solidSpots.Contains((level, new Vector2i(x, y))))
                    return false;

                var point = new Vector2(x + 0.5f, y + 0.5f);
                if (_map.TryFindGridAt(level, point, out var gridUid, out var grid) &&
                    _map.TryGetTileRef(gridUid, grid, _map.WorldToTile(gridUid, grid, point), out var tileRef) &&
                    !tileRef.Tile.IsEmpty)
                {
                    return false;
                }
            }
        }

        return true;
    }

    [SubscribeLocalEvent]
    private void OnZMapStartup(Entity<ST14ZMapComponent> ent, ref ComponentStartup args) => InvalidateGroundCache();

    [SubscribeLocalEvent]
    private void OnZMapShutdown(Entity<ST14ZMapComponent> ent, ref ComponentShutdown args) => InvalidateGroundCache();

    [SubscribeLocalEvent]
    private void OnZMapState(Entity<ST14ZMapComponent> ent, ref AfterAutoHandleStateEvent args) => InvalidateGroundCache();

    [SubscribeLocalEvent]
    private void OnHighGroundMapInit(Entity<ST14ZLevelHighGroundComponent> ent, ref MapInitEvent args)
    {
        InvalidateGroundAt(ent);
    }

    [SubscribeLocalEvent]
    private void OnHighGroundAnchor(Entity<ST14ZLevelHighGroundComponent> ent, ref AnchorStateChangedEvent args)
    {
        InvalidateGroundAt(ent);
    }

    [SubscribeLocalEvent]
    private void OnHighGroundMove(Entity<ST14ZLevelHighGroundComponent> ent, ref MoveEvent args) => InvalidateGroundAt(ent);

    [SubscribeLocalEvent]
    private void OnHighGroundShutdown(Entity<ST14ZLevelHighGroundComponent> ent, ref ComponentShutdown args)
    {
        // The component is going away, so the ground under it must be recomputed without it. Bump the epoch
        // instead of computing now: during shutdown the anchored-entities enumerator still returns it.
        _groundCacheEpoch++;
    }

    /// <param name="force">Recompute even if the body is still on the same tile (tile/map changed under it).</param>
    private void RequestCacheMovement(Entity<ST14ZPhysicsComponent> entity, bool force = true)
    {
        var xform = _transformQuery.GetComponent(entity);
        var tile = _transform.GetGridOrMapTilePosition(entity, xform);
        var parent = xform.GridUid ?? xform.MapUid ?? EntityUid.Invalid;
        var comp = entity.Comp;

        // ST14: flat ground only depends on which tile of which grid/map the body is on, so moving
        // inside the same tile reuses it. High ground (stairs) depends on the exact position and on the
        // vertical velocity, so it is always recomputed, as before.
        if (!force
            && !comp.CachedGroundDynamic
            && tile == comp.CachedTile
            && parent == comp.CachedTileParent
            && comp.CachedGroundEpoch == _groundCacheEpoch)
            return;

        comp.CachedTile = tile;
        comp.CachedTileParent = parent;
        comp.CachedGroundEpoch = _groundCacheEpoch;
        comp.CachedGroundHeight = ComputeGroundHeightInternal((entity, comp), out var sticky, out var dynamic);
        comp.CachedStickyGround = sticky;
        comp.CachedGroundDynamic = dynamic;
    }

    [SubscribeLocalEvent]
    private void OnMoveEvent(Entity<ST14ZPhysicsComponent> entity, ref MoveEvent args)
    {
        // ST14: turning in place (mobs follow the mouse every frame) changes neither the tile, nor the
        // ground, nor whether the body should be awake; only real position/parent changes matter.
        if (args.NewPosition.EntityId == args.OldPosition.EntityId && args.NewPosition.Position == args.OldPosition.Position)
            return;

        if (!_dirtyMovementSet.Add(entity))
            return;

        _dirtyMovementBodies.Add(entity);
    }

    [SubscribeLocalEvent]
    private void OnZLevelMapMove(Entity<ST14ZPhysicsComponent> ent, ref ST14ZLevelMapMoveEvent args)
    {
        ent.Comp.CurrentZLevel = args.CurrentZLevel;
        DirtyField(ent, ent.Comp, nameof(ST14ZPhysicsComponent.CurrentZLevel));
        RequestCacheMovement(ent);
    }

    /// <summary>
    /// Computes the "ground height" relative to the entity's current Z-level.
    /// Returns values where 0 means ground on the same level, -1 means ground one level below,
    /// and intermediate values are possible for high ground entities (stairs).
    /// </summary>
    private float ComputeGroundHeightInternal(Entity<ST14ZPhysicsComponent?> target, out bool stickyGround, out bool dynamic, int maxFloors = 1)
    {
        stickyGround = false;
        dynamic = false; // ST14: set when the result came from high ground and so depends on the exact position

        if (!Resolve(target, ref target.Comp, false))
            return 0;

        var xform = _transformQuery.GetComponent(target); // ST14: cached query
        if (!_zMapQuery.TryComp(xform.MapUid, out var zMapComp))
            return 0;

        var worldPos = _transform.GetWorldPosition(xform); // ST14: reuse the resolved xform

        //Select current map by default
        Entity<ST14ZMapComponent> checkingMap = (xform.MapUid.Value, zMapComp);

        for (var floor = 0; floor <= maxFloors; floor++)
        {
            if (floor != 0) //Select map below
            {
                // ST14: only what fits through the opening falls into it: a body wider than a trench stays on its edge
                if (!FitsThrough(target.Owner, checkingMap.Owner, worldPos))
                {
                    dynamic = true;
                    return -(floor - 1);
                }

                if (!TryMapOffset((checkingMap.Owner, checkingMap.Comp), -floor, out var tempCheckingMap))
                    return -(floor - 1) - 0.1f; //So we still chasming down, but visually walking on same level

                checkingMap = tempCheckingMap;
            }

            // ST14: a roof is ground to walk on. On this level it is the floor (a bunker roof up on +1); on the level
            // below it sticks up out of the ground (a dug-out roof), so whoever climbed onto it stands a level higher,
            // as on a wall top
            if (_solidSpots.Count > 0 &&
                _solidSpots.Contains((checkingMap.Owner, new Vector2i((int) MathF.Floor(worldPos.X), (int) MathF.Floor(worldPos.Y)))))
            {
                return floor == 0 ? 0 : -floor + 1;
            }

            //Find whichever grid (structure or planet) provides the floor here.
            if (!_map.TryFindGridAt(checkingMap, worldPos, out var gridUid, out var grid))
                continue;

            var gridTile = _map.WorldToTile(gridUid, grid, worldPos);

            //Check all types of ZHeight entities
            var query = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, gridTile);
            while (query.MoveNext(out var uid))
            {
                if (!_zHighGroundQuery.TryComp(uid, out var heightComp))
                    continue;

                // ST14-start (Sh1ntra): an underpass only holds up whoever is already above it
                if (heightComp.Underpass && floor == 0)
                    continue;
                // ST14-end

                var dir = _transformQuery.GetComponent(uid.Value).LocalRotation.GetCardinalDir(); // ST14: cached query

                var gridLocal = _map.WorldToLocal(gridUid, grid, worldPos);
                var local = new Vector2((gridLocal.X % 1 + 1) % 1, (gridLocal.Y % 1 + 1) % 1);

                var t = dir switch
                {
                    Direction.East => heightComp.Corner ? (local.X + 1f - local.Y) / 2f : local.X,
                    Direction.West => heightComp.Corner ? (1f - local.X + local.Y) / 2f : 1f - local.X,
                    Direction.North => heightComp.Corner ? (local.X + local.Y) / 2f : local.Y,
                    Direction.South => heightComp.Corner ? (1f - local.X + 1f - local.Y) / 2f : 1f - local.Y,
                    _ => 0.5f,
                };

                t = float.Clamp(t, 0f, 1f);

                var curve = heightComp.HeightCurve;
                if (curve.Count == 0)
                    continue;

                if (curve.Count == 1)
                {
                    var groundY = curve[0];
                    // groundHeight is negative downwards: -floor + groundY
                    dynamic = true; // ST14
                    return -floor + groundY;
                }

                var step = 1f / (curve.Count - 1);
                var index = (int)(t / step);
                var frac = (t - index * step) / step;

                var y0 = curve[Math.Clamp(index, 0, curve.Count - 1)];
                var y1 = curve[Math.Clamp(index + 1, 0, curve.Count - 1)];

                var groundYInterp = MathHelper.Lerp(y0, y1, frac);

                if (target.Comp.Velocity < 0 && target.Comp.Velocity > -2f && heightComp.Stick)
                    stickyGround = true;

                dynamic = true; // ST14

                return -floor + groundYInterp;
            }

            //No ZEntities found, check floor tiles
            if (_map.TryGetTileRef(gridUid, grid, gridTile, out var tileRef) &&
                !tileRef.Tile.IsEmpty)
                return -floor; // tile ground has groundY == 0 -> -floor
        }

        return -maxFloors;
    }

    /// <summary>
    /// Checks whether there is a ceiling above the specified entity (tiles on the layer above).
    /// If there are no Z-levels above, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(EntityUid ent, Entity<ST14ZMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return false;

        if (!TryMapUp(currentMapUid.Value, out var mapAboveUid))
            return false;

        var worldPos = _transform.GetWorldPosition(ent);
        if (!_map.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        if (_map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef) &&
            !tileRef.Tile.IsEmpty)
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether there is a ceiling above the specified entity (tiles on the layer above).
    /// If there are no Z-levels above, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(Vector2i indices, Entity<ST14ZMapComponent?> map)
    {
        if (!Resolve(map, ref map.Comp, false))
            return false;

        if (!TryMapUp(map, out var mapAboveUid))
            return false;

        if (!_gridQuery.TryComp(mapAboveUid, out var mapAboveGrid))
            return false;

        if (_map.TryGetTileRef(mapAboveUid, mapAboveGrid, indices, out var tileRef) &&
            !tileRef.Tile.IsEmpty)
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether any grid on the map above has a non-empty tile at the given world position.
    /// World-position overload; see also <see cref="HasTileAbove(EntityUid, Entity{ST14ZMapComponent?}?)"/>
    /// and <see cref="HasTileAbove(Vector2i, Entity{ST14ZMapComponent?})"/>.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(Vector2 worldPos, Entity<ST14ZMapComponent?> currentMap)
    {
        if (!TryMapUp(currentMap, out var mapAboveUid))
            return false;

        if (!_map.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        return _map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    [PublicAPI]
    public void SetZPosition(Entity<ST14ZPhysicsComponent?> ent, float newPosition)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var changed = ent.Comp.LocalPosition != newPosition; // ST14: don't dirty an unchanged field
        ent.Comp.LocalPosition = newPosition;
        if (changed)
            DirtyField(ent, ent.Comp, nameof(ST14ZPhysicsComponent.LocalPosition));
        WakeBody(ent);
    }

    [PublicAPI]
    public void UpdateGravityState(Entity<ST14ZPhysicsComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var ev = new ST14CheckGravityEvent();
        RaiseLocalEvent(ent.Owner, ev);

        SetZGravity(ent, ev.Gravity);
    }

    private void SetZGravity(Entity<ST14ZPhysicsComponent?> ent, float newGravityMultiplier)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var changed = ent.Comp.GravityMultiplier != newGravityMultiplier; // ST14
        ent.Comp.GravityMultiplier = newGravityMultiplier;
        if (changed)
            DirtyField(ent, ent.Comp, nameof(ST14ZPhysicsComponent.GravityMultiplier));
        WakeBody(ent);
    }

    /// <summary>
    /// Sets the vertical velocity for the entity. Positive values make the entity fly upward. Negative values make it fly downward.
    /// </summary>
    [PublicAPI]
    public void SetZVelocity(Entity<ST14ZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var changed = ent.Comp.Velocity != newVelocity; // ST14
        ent.Comp.Velocity = newVelocity;
        if (changed)
            DirtyField(ent, ent.Comp, nameof(ST14ZPhysicsComponent.Velocity));
        WakeBody(ent);
    }

    /// <summary>
    /// Add the vertical velocity for the entity. Positive values make the entity fly upward. Negative values make it fly downward.
    /// </summary>
    [PublicAPI]
    public void AddZVelocity(Entity<ST14ZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        ent.Comp.Velocity += newVelocity;
        if (newVelocity != 0) // ST14
            DirtyField(ent, ent.Comp, nameof(ST14ZPhysicsComponent.Velocity));
        WakeBody(ent);
    }

    [PublicAPI]
    public bool TryMove(EntityUid ent, int offset, Entity<ST14ZMapComponent?>? map = null)
    {
        map ??= Transform(ent).MapUid;

        if (map is null)
            return false;

        if (!TryMapOffset(map.Value, offset, out var targetMap))
            return false;

        if (!_mapQuery.TryComp(targetMap, out var targetMapComp))
            return false;

        var worldRot = _transform.GetWorldRotation(ent);

        var beforeEv = new ST14ZLevelBeforeMapMoveEvent(offset, targetMap.Comp.Depth);
        RaiseLocalEvent(ent, ref beforeEv);

        _transform.SetMapCoordinates(ent, new MapCoordinates(_transform.GetWorldPosition(ent), targetMapComp.MapId));
        _transform.SetWorldRotation(ent, worldRot);

        var ev = new ST14ZLevelMapMoveEvent(offset, targetMap.Comp.Depth);
        RaiseLocalEvent(ent, ref ev);

        return true;
    }

    [PublicAPI]
    public bool TryMoveUp(EntityUid ent) => TryMove(ent, 1);

    [PublicAPI]
    public bool TryMoveDown(EntityUid ent)
    {
        return TryMove(ent, -1);
    }

    [PublicAPI]
    public bool TryMoveDownOrChasm(EntityUid ent)
    {
        if (TryMoveDown(ent))
            return true;

        if (Transform(ent).MapUid is { } map)
        {
            var ev = new ST14ZLevelFallOutOfBoundsEvent(ent);
            RaiseLocalEvent(map, ref ev);
        }

        return false;
    }

    private void UpdateDirtyMovement()
    {
        for (var i = _dirtyMovementBodies.Count - 1; i >= 0; i--)
        {
            var uid = _dirtyMovementBodies[i];

            if (!ZPhysicsQuery.TryComp(uid, out var component))
                continue;

            var entity = (uid, component);
            RequestCacheMovement(entity, force: false); // ST14: reuse flat ground while on the same tile
            RefreshBody(entity);
        }

        _dirtyMovementBodies.Clear();
        _dirtyMovementSet.Clear(); // ST14
    }
}

/// <summary>
/// Is called on an entity right before it moves between z-levels.
/// </summary>
/// <param name="offset">How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.</param>
[ByRefEvent]
public struct ST14ZLevelBeforeMapMoveEvent(int offset, int level)
{
    /// <summary>
    /// How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.
    /// </summary>
    public int Offset = offset;

    public int CurrentZLevel = level;
}

/// <summary>
/// Is called on an entity when it moves between z-levels.
/// </summary>
/// <param name="offset">How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.</param>
[ByRefEvent]
public struct ST14ZLevelMapMoveEvent(int offset, int level)
{
    /// <summary>
    /// How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.
    /// </summary>
    public int Offset = offset;

    public int CurrentZLevel = level;
}

/// <summary>
/// Is triggered when an entity falls to the lower z-levels under the force of gravity
/// </summary>
[ByRefEvent]
public struct ST14ZLevelFallMapEvent;

/// <summary>
///Called upon the essence before attempting to fall into the abyss
/// </summary>
public sealed class ST14ZLevelChasmAttempt(EntityUid falled) : CancellableEntityEventArgs, IInventoryRelayEvent
{
    public EntityUid Falled = falled;
    public SlotFlags TargetSlots => SlotFlags.All;
}

/// <summary>
/// Raised on a map entity when an entity standing on it fails to move one Z-level down (there's
/// nothing lower in the stack). Purely a notification — the core Z-level system has no opinion on
/// whether this is lethal. See <see cref="Content.Shared._ST14.ZLevels.Chasm.ST14ZLevelChasmComponent"/>.
/// </summary>
[ByRefEvent]
public struct ST14ZLevelFallOutOfBoundsEvent(EntityUid player)
{
    public EntityUid Player = player;
}

/// <summary>
/// It is called on an entity when it hits the floor or ceiling with force.
/// </summary>
/// <param name="impactPower">The speed at the moment of impact. Always positive</param>
[ByRefEvent]
public struct ST14ZLevelHitEvent(float impactPower)
{
    /// <summary>
    /// The speed at the moment of impact. Always positive
    /// </summary>
    public float ImpactPower = impactPower;
}

/// <summary>
/// Is called every frame to calculate the current vertical velocity of the active zphysics entities.
/// </summary>
[ByRefEvent]
public struct ST14GetZVelocityEvent(Entity<ST14ZPhysicsComponent> target)
{
    public Entity<ST14ZPhysicsComponent> Target = target;
    public float VelocityDelta = 0;
}

/// <summary>
/// Called when UpdateGravityState is used to update the current strength of the active z-level gravity. Various systems can subscribe to this to disable gravity.
/// </summary>
public sealed class ST14CheckGravityEvent : EntityEventArgs
{
    public float Gravity = 1f;
}

