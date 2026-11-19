using Content.Shared._ST14.Hellwalls;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Map;

namespace Content.Client._ST14.HellWalls;

public sealed partial class ST14HellWallSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const int North = 1;
    private const int South = 2;
    private const int East = 4;
    private const int West = 8;

    private static readonly (int Bit, Vector2i Offset)[] Directions =
    {
        (North, new Vector2i(0, 1)),
        (South, new Vector2i(0, -1)),
        (East, new Vector2i(1, 0)),
        (West, new Vector2i(-1, 0)),
    };

    private readonly HashSet<EntityUid> _dirtyGrids = new();
    private readonly HashSet<EntityUid> _checkGrids = new();
    private readonly Dictionary<EntityUid, int> _gridTurns = new();
    private readonly Dictionary<Vector2i, Entity<ST14HellWallComponent>> _cells = new();
    private readonly Dictionary<Vector2i, int> _masks = new();
    private readonly Dictionary<Vector2i, char> _horizontal = new();
    private readonly Dictionary<Vector2i, char> _vertical = new();
    private readonly List<Vector2i> _run = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14HellWallComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ST14HellWallComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14HellWallComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<ST14HellWallComponent> ent, ref ComponentStartup args)
    {
        MarkDirty(ent);

        if (ent.Comp.Preview is { } layer && Transform(ent).MapID != MapId.Nullspace)
            _sprite.LayerSetVisible(ent.Owner, layer, false);
    }

    private void OnAnchorChanged(Entity<ST14HellWallComponent> ent, ref AnchorStateChangedEvent args)
    {
        MarkDirty(ent);
    }

    private void OnShutdown(Entity<ST14HellWallComponent> ent, ref ComponentShutdown args)
    {
        MarkDirty(ent);
    }

    private void MarkDirty(EntityUid uid)
    {
        if (Transform(uid).GridUid is { } grid)
            _dirtyGrids.Add(grid);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _checkGrids.Clear();
        _checkGrids.UnionWith(_gridTurns.Keys);
        _checkGrids.UnionWith(_dirtyGrids);

        foreach (var grid in _checkGrids)
        {
            if (TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out var gridComp))
            {
                _gridTurns.Remove(grid);
                continue;
            }

            var turns = GetTurns(grid);
            if (!_dirtyGrids.Contains(grid) && _gridTurns.TryGetValue(grid, out var old) && old == turns)
                continue;

            if (UpdateGrid((grid, gridComp), turns))
                _gridTurns[grid] = turns;
            else
                _gridTurns.Remove(grid);
        }

        _dirtyGrids.Clear();
    }

    private int GetTurns(EntityUid grid)
    {
        var angle = _eye.CurrentEye.Rotation + _transform.GetWorldRotation(grid);
        var turns = (int) Math.Round(angle.Theta / (Math.PI / 2));
        return (turns % 4 + 4) % 4;
    }

    private bool UpdateGrid(Entity<MapGridComponent> grid, int turns)
    {
        _cells.Clear();
        _masks.Clear();
        _horizontal.Clear();
        _vertical.Clear();

        var query = EntityQueryEnumerator<ST14HellWallComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wall, out var xform))
        {
            if (xform.GridUid != grid.Owner || !xform.Anchored)
                continue;

            var tile = _map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates);
            _cells[Rotate(tile, turns)] = (uid, wall);
        }

        if (_cells.Count == 0)
            return false;

        foreach (var (cell, wall) in _cells)
        {
            var mask = 0;
            foreach (var (bit, offset) in Directions)
            {
                if (_cells.TryGetValue(cell + offset, out var other) && other.Comp.Key == wall.Comp.Key)
                    mask |= bit;
            }

            _masks[cell] = mask;
        }

        foreach (var cell in _cells.Keys)
        {
            if (!_horizontal.ContainsKey(cell))
                FillRun(cell, true);

            if (!_vertical.ContainsKey(cell))
                FillRun(cell, false);
        }

        foreach (var (cell, wall) in _cells)
        {
            var state = wall.Comp.Landing
                ? $"{wall.Comp.Prefix}{_masks[cell]}"
                : State(wall.Comp.Prefix, _masks[cell], Parity(cell.X), Parity(cell.Y), _horizontal[cell], _vertical[cell]);

            _sprite.LayerSetRsiState(wall.Owner, 0, state);
        }

        return true;
    }

    private void FillRun(Vector2i cell, bool horizontal)
    {
        var forward = horizontal ? East : North;
        var backward = horizontal ? West : South;
        var step = horizontal ? new Vector2i(1, 0) : new Vector2i(0, 1);

        var start = cell;
        while ((_masks[start] & backward) != 0)
        {
            start -= step;
        }

        _run.Clear();
        _run.Add(start);
        while ((_masks[_run[^1]] & forward) != 0)
        {
            _run.Add(_run[^1] + step);
        }

        var side = Vote(horizontal);
        var target = horizontal ? _horizontal : _vertical;
        foreach (var member in _run)
        {
            target[member] = side;
        }
    }

    private char Vote(bool horizontal)
    {
        var first = 0;
        var second = 0;
        var both = 0;

        foreach (var cell in _run)
        {
            var mask = _masks[cell];
            var n = (mask & North) != 0;
            var s = (mask & South) != 0;
            var e = (mask & East) != 0;
            var w = (mask & West) != 0;

            if (horizontal)
            {
                if (s && !n)
                    first++;
                else if (n && !s)
                    second++;
                else if (n && s && e != w)
                    both++;
            }
            else
            {
                if (e && !w)
                    first++;
                else if (w && !e)
                    second++;
                else if (e && w && n != s)
                    both++;
            }
        }

        var fallback = horizontal ? 's' : 'b';
        var best = Math.Max(first, Math.Max(second, both));
        if (best == 0)
            return fallback;

        var sided = 0;
        var letter = fallback;
        if (first == best)
        {
            sided++;
            letter = horizontal ? 'n' : 'l';
        }

        if (second == best)
        {
            sided++;
            letter = horizontal ? 's' : 'r';
        }

        return sided switch
        {
            0 => 'b',
            1 => letter,
            _ => fallback,
        };
    }

    private static string State(string prefix, int mask, int h, int v, char hs, char vs)
    {
        if ((mask & (East | West)) == 0)
        {
            h = 0;
            hs = 's';
        }
        else if ((mask & (North | South)) == 0)
        {
            v = 0;
            vs = 'b';
        }

        if ((mask & East) != 0 && vs == 'r' || (mask & West) != 0 && vs == 'l')
            vs = 'b';

        return $"{prefix}{mask}-{h}{v}-{hs}{vs}";
    }

    private static Vector2i Rotate(Vector2i tile, int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            tile = new Vector2i(-tile.Y, tile.X);
        }

        return tile;
    }

    private static int Parity(int value)
    {
        return (value % 2 + 2) % 2;
    }
}
