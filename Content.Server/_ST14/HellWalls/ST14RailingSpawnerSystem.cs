using Content.Server._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.Hellwalls;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server._ST14.Hellwalls;

public sealed partial class ST14RailingSpawnerSystem : EntitySystem
{
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    private readonly HashSet<Entity<ST14RailingComponent>> _railings = new();
    private EntityUid? _removed;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14RailingSpawnerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14RailingSpawnerComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14RailingSpawnerComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<ST14RailingSpawnerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ST14ZLevelMapNetworkUpdatedEvent>(OnNetworkUpdated);
    }

    private void OnTerminating(Entity<ST14RailingSpawnerComponent> ent, ref EntityTerminatingEvent args)
    {
        RefreshWithout(ent);
    }

    private void RefreshWithout(Entity<ST14RailingSpawnerComponent> ent)
    {
        _removed = ent;
        Refresh(ent);
        _removed = null;
    }
    private void OnMapInit(Entity<ST14RailingSpawnerComponent> ent, ref MapInitEvent args)
    {
        Refresh(ent);
    }

    private void OnAnchorChanged(Entity<ST14RailingSpawnerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || args.Detaching)
            return;

        Refresh(ent);
    }

    private void OnShutdown(Entity<ST14RailingSpawnerComponent> ent, ref ComponentShutdown args)
    {
        RefreshWithout(ent);
    }

    private void OnNetworkUpdated(ST14ZLevelMapNetworkUpdatedEvent args)
    {
        var query = EntityQueryEnumerator<ST14RailingSpawnerComponent>();
        while (query.MoveNext(out var uid, out var spawner))
        {
            if (LifeStage(uid) >= EntityLifeStage.MapInitialized)
                Refresh((uid, spawner));
        }
    }

    private void Refresh(Entity<ST14RailingSpawnerComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid ||
            TerminatingOrDeleted(gridUid) ||
            !TryComp<MapGridComponent>(gridUid, out var gridComp) ||
            xform.MapUid is not { } mapUid ||
            !_zLevels.TryMapOffset(mapUid, 1, out var above, out var aboveMap) ||
            TerminatingOrDeleted(above))
            return;

        Entity<MapGridComponent> grid = (gridUid, gridComp);
        var origin = _map.TileIndicesFor(gridUid, gridComp, xform.Coordinates);

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                RefreshRailing(ent.Comp, grid, aboveMap.MapId, origin + new Vector2i(x, y));
            }
        }
    }

    private void RefreshRailing(ST14RailingSpawnerComponent spawner, Entity<MapGridComponent> grid, MapId above, Vector2i tile)
    {
        var needed = !HasHighGround(grid, tile) && HasHighGroundAround(grid, tile);
        var center = _map.GridTileToWorldPos(grid.Owner, grid.Comp, tile);
        var railing = FindRailing(grid, tile, new MapCoordinates(center, above));

        if (!needed)
        {
            if (railing is { } existing)
                QueueDel(existing);

            return;
        }

        var lift = NextToWall(grid, tile) ? spawner.Lift : Vector2.Zero;
        var local = new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * grid.Comp.TileSize + lift;
        var position = _map.LocalToWorld(grid.Owner, grid.Comp, local);

        if (railing is { } current)
            _transform.SetWorldPosition(current, position);
        else
            Spawn(spawner.Railing, new MapCoordinates(position, above));
    }

    private bool NextToWall(Entity<MapGridComponent> grid, Vector2i tile)
    {
        return IsWall(grid, tile + new Vector2i(0, 1)) || IsWall(grid, tile + new Vector2i(0, -1));
    }

    private bool IsWall(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (uid != _removed && TryComp<ST14HellWallComponent>(uid, out var wall) && !wall.Landing)
                return true;
        }

        return false;
    }

    private bool HasHighGroundAround(Entity<MapGridComponent> grid, Vector2i tile)
    {
        foreach (var offset in Neighbours)
        {
            if (HasHighGround(grid, tile + offset))
                return true;
        }

        return false;
    }

    private bool HasHighGround(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (uid != _removed && HasComp<ST14ZLevelHighGroundComponent>(uid))
                return true;
        }

        return false;
    }

    private EntityUid? FindRailing(Entity<MapGridComponent> grid, Vector2i tile, MapCoordinates coords)
    {
        _railings.Clear();
        _lookup.GetEntitiesInRange(coords, grid.Comp.TileSize / 2f, _railings);

        foreach (var railing in _railings)
        {
            if (TerminatingOrDeleted(railing))
                continue;

            if (_map.WorldToTile(grid.Owner, grid.Comp, _transform.GetWorldPosition(railing)) == tile)
                return railing.Owner;
        }

        return null;
    }
}
