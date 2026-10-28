using Content.Server.GameTicking;
using Content.Shared._ST14.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Tiles;

/// <summary>
/// Gives multi-tile floors the variant that matches their grid position, so neighbouring tiles
/// line up into one big texture. Changes are queued and applied on the next update so the tile is
/// never rewritten from inside someone else's SetTiles call
/// </summary>
public sealed partial class ST14MultiTileSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDef = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private readonly Dictionary<int, ST14MultiTilePrototype> _byTileId = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _pending = new();
    private bool _cached;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ST14MultiTilePrototype>())
            _cached = false;
    }

    /// <summary>
    /// Built on first use, tile definitions are not registered yet when systems initialize
    /// </summary>
    private void EnsureCache()
    {
        if (_cached)
            return;

        _cached = true;
        _byTileId.Clear();
        foreach (var multi in _proto.EnumeratePrototypes<ST14MultiTilePrototype>())
        {
            if (!_tileDef.TryGetDefinition(multi.Tile, out var def))
                continue;

            if (def.Variants < multi.Width * multi.Height)
            {
                Log.Error($"Tile {multi.Tile} has {def.Variants} variants but {multi.ID} needs {multi.Width * multi.Height}");
                continue;
            }

            _byTileId[def.TileId] = multi;
        }
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        EnsureCache();
        foreach (var change in args.Changes)
        {
            if (_byTileId.ContainsKey(change.NewTile.TypeId))
                Queue(args.Entity, change.GridIndices);
        }
    }

    private void OnPostGameMapLoad(PostGameMapLoad args)
    {
        foreach (var grid in args.Grids)
        {
            FixGrid(grid);
        }
    }

    /// <summary>
    /// Re-picks the variant of every multi-tile floor on the grid, for grids saved before the floor became a multi-tile
    /// </summary>
    public void FixGrid(EntityUid uid)
    {
        EnsureCache();
        if (_byTileId.Count == 0 || !TryComp<MapGridComponent>(uid, out var grid))
            return;

        var enumerator = _map.GetAllTilesEnumerator(uid, grid);
        while (enumerator.MoveNext(out var tileRef))
        {
            if (_byTileId.ContainsKey(tileRef.Value.Tile.TypeId))
                Queue(uid, tileRef.Value.GridIndices);
        }
    }

    private void Queue(EntityUid grid, Vector2i indices)
    {
        if (!_pending.TryGetValue(grid, out var set))
        {
            set = new HashSet<Vector2i>();
            _pending[grid] = set;
        }

        set.Add(indices);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        // SetTiles raises TileChangedEvent again, which queues into _pending, so work on a snapshot.
        // The re-queued tiles already have the right variant and are skipped next update.
        var pending = new List<(EntityUid, HashSet<Vector2i>)>();
        foreach (var (uid, set) in _pending)
        {
            pending.Add((uid, set));
        }
        _pending.Clear();

        var changes = new List<(Vector2i, Tile)>();
        foreach (var (uid, set) in pending)
        {
            if (!TryComp<MapGridComponent>(uid, out var grid))
                continue;

            changes.Clear();
            foreach (var indices in set)
            {
                var tile = _map.GetTileRef(uid, grid, indices).Tile;
                if (!_byTileId.TryGetValue(tile.TypeId, out var multi))
                    continue;

                var variant = GetVariant(multi, indices);
                if (tile.Variant == variant && tile.RotationMirroring == 0)
                    continue;

                changes.Add((indices, new Tile(tile.TypeId, tile.Flags, variant)));
            }

            if (changes.Count > 0)
                _map.SetTiles(uid, grid, changes);
        }
    }

    /// <summary>
    /// Variants go row by row from the top left of the big texture, while grid Y grows upwards
    /// </summary>
    public static byte GetVariant(ST14MultiTilePrototype multi, Vector2i indices)
    {
        var column = Mod(indices.X, multi.Width);
        var row = multi.Height - 1 - Mod(indices.Y, multi.Height);
        return (byte) (row * multi.Width + column);
    }

    private static int Mod(int value, int size)
    {
        var result = value % size;
        return result < 0 ? result + size : result;
    }
}
