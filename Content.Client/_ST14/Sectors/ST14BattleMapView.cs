using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The sectors of the battle laid side by side as one flat map, each turned so its passages meet the neighbour they lead
/// to. The planet is a sphere and cannot lie flat as a whole, the few sectors of a battle can. The wheel zooms, the left
/// button drags the map or draws with a tool, the right button puts the tool away
/// </summary>
public sealed class ST14BattleMapView : Control
{
    private const float DragThreshold = 6f;
    private const float MaxZoom = 8f;

    private static readonly Color Back = Color.FromHex("#0C0D0B");
    private static readonly Color Unknown = Color.FromHex("#26241F");
    private static readonly Color Open = Color.FromHex("#6FE08A");
    private static readonly Color Closed = Color.FromHex("#A8A49A");
    private static readonly Color Mate = Color.FromHex("#5CD6FF");

    private readonly IResourceCache _cache;
    private readonly ST14BattleMapClientSystem _system;
    private readonly SpriteSystem _sprite;
    private readonly ST14VertexBuffer _buffer = new();

    /// <summary>
    /// Where each sector lies on the flat map and how far it is turned, in sixths of a turn counter-clockwise
    /// </summary>
    private readonly Dictionary<int, (Vector2 Position, int Turn)> _layout = new();
    private string _layoutKey = string.Empty;

    private Vector2 _camera;
    private float _zoom = 2f;
    private bool _framed;
    private float _time;
    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;
    private Vector2 _lastPos;

    public ST14WarMarkKind? Tool;
    public bool Eraser;

    public event Action<ST14BattleMark>? OnMarkPlaced;
    public event Action<int>? OnMarkErased;
    public event Action? OnToolCancelled;

    /// <summary>
    /// The wheel turned on to zoom out once the whole battle was in view: the sector in the middle of the view
    /// </summary>
    public event Action<int?>? OnZoomPastMin;

    public event Action<int?>? OnCellHovered;
    public event Action<int>? OnCellClicked;

    private int? _hovered;

    public ST14BattleMapView(IResourceCache cache, ST14BattleMapClientSystem system, SpriteSystem sprite)
    {
        _cache = cache;
        _system = system;
        _sprite = sprite;
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
    }

    private bool Drawing => Tool != null || Eraser;

    private Vector2 Middle => (Vector2) PixelSize / 2;

    private ST14BattleSector? Sector(int cell)
    {
        return _system.State?.Sectors.FirstOrDefault(s => s.Cell == cell) is { Code: not null } sector ? sector : null;
    }

    #region Layout

    private void UpdateLayout(ST14BattleMapStateEvent state)
    {
        var anchor = state.Here >= 0 ? state.Here : state.Target >= 0 ? state.Target : state.Sectors.FirstOrDefault(s => s.Loaded).Cell;
        var key = $"{anchor}:{string.Join(',', state.Sectors.Where(s => s.Loaded).Select(s => s.Cell))}";
        if (key == _layoutKey)
            return;

        _layoutKey = key;
        _layout.Clear();
        _layout[anchor] = (Vector2.Zero, 0);

        var queue = new Queue<int>();
        queue.Enqueue(anchor);
        while (queue.TryDequeue(out var cell))
        {
            if (Sector(cell) is not { Loaded: true } sector)
                continue;

            var (position, turn) = _layout[cell];
            for (var edge = 0; edge < sector.Edges.Length; edge++)
            {
                var next = sector.Edges[edge];
                if (_layout.ContainsKey(next))
                    continue;

                var direction = (edge + turn) % 6;
                var at = position + ST14SectorGeometry.EdgeNormal(direction) * ST14SectorGeometry.Apothem * 2;

                // around the corners of the planet two sectors can want the same place, the first one keeps it
                if (_layout.Values.Any(v => (v.Position - at).Length() < ST14SectorGeometry.Apothem))
                    continue;

                // the neighbour turns until its passage back faces this sector
                var nextTurn = 0;
                if (Sector(next) is { Loaded: true } neighbour && Array.IndexOf(neighbour.Edges, cell) is var back and >= 0)
                    nextTurn = ((direction + 3 - back) % 6 + 6) % 6;

                _layout[next] = (at, nextTurn);
                queue.Enqueue(next);
            }
        }
    }

    private static Vector2 Rotate(Vector2 v, int turn)
    {
        var angle = MathF.PI / 3 * turn;
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    /// <summary>
    /// A point in the tiles of a sector on the flat map
    /// </summary>
    private Vector2? ToWorld(int cell, Vector2 local)
    {
        if (!_layout.TryGetValue(cell, out var place))
            return null;

        return place.Position + Rotate(local - ST14SectorGeometry.Center, place.Turn);
    }

    private Vector2 ToLocal(int cell, Vector2 world)
    {
        var place = _layout[cell];
        return ST14SectorGeometry.Center + Rotate(world - place.Position, -place.Turn);
    }

    private Vector2 ToScreen(Vector2 world)
    {
        var offset = (world - _camera) * _zoom;
        return Middle + new Vector2(offset.X, -offset.Y);
    }

    private Vector2 ScreenToWorld(Vector2 pixel)
    {
        var offset = (pixel - Middle) / _zoom;
        return _camera + new Vector2(offset.X, -offset.Y);
    }

    /// <summary>
    /// The loaded sector under a point of the flat map
    /// </summary>
    private int? CellAt(Vector2 world)
    {
        foreach (var (cell, _) in _layout)
        {
            if (Sector(cell) is { Loaded: true } && ST14SectorGeometry.Contains(ToLocal(cell, world)))
                return cell;
        }

        return null;
    }

    /// <summary>
    /// The sector on the flat map a point lies in, loaded or not
    /// </summary>
    private int? NearestCell(Vector2 world)
    {
        int? best = null;
        var bestDistance = ST14SectorGeometry.Radius;
        foreach (var (cell, place) in _layout)
        {
            var distance = (place.Position - world).Length();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = cell;
        }

        return best;
    }

    /// <summary>
    /// Puts a sector in the middle of the view, close enough to read its ground
    /// </summary>
    public void Focus(int cell)
    {
        // a hidden view has no size yet, the focus waits for the first frame it is drawn in
        _pendingFocus = cell;
        _framed = true;
    }

    private int? _pendingFocus;

    private void ApplyFocus()
    {
        if (_pendingFocus is not { } cell || PixelSize.X <= 0 || !_layout.TryGetValue(cell, out var place))
            return;

        _camera = place.Position;
        _zoom = FitZoom();
        _pendingFocus = null;
    }

    /// <summary>
    /// Frames the map again on the player, or on the battle when the player is not in it
    /// </summary>
    public void Reframe()
    {
        _framed = false;
    }

    /// <summary>
    /// The whole battle in view: every loaded sector and a little of what lies around
    /// </summary>
    private void FrameBattle()
    {
        var loaded = _layout.Where(p => Sector(p.Key) is { Loaded: true }).Select(p => p.Value.Position).ToList();
        if (loaded.Count == 0)
            return;

        var min = loaded.Aggregate(Vector2.Min) - new Vector2(ST14SectorGeometry.Radius * 1.15f);
        var max = loaded.Aggregate(Vector2.Max) + new Vector2(ST14SectorGeometry.Radius * 1.15f);
        _camera = (min + max) / 2;
        _zoom = MathF.Max(0.1f, MathF.Min(PixelSize.X / (max.X - min.X), PixelSize.Y / (max.Y - min.Y)));
        _framed = PixelSize.X > 0;
    }

    private float FitZoom()
    {
        return MathF.Max(0.1f, MathF.Min(PixelSize.X, PixelSize.Y) * 0.85f / (ST14SectorGeometry.Radius * 2));
    }

    #endregion

    #region Input

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        var pixel = args.RelativePixelPosition;
        var before = ScreenToWorld(pixel);
        var min = FitZoom() / (1 + _layout.Count * 0.5f);
        if (args.Delta.Y < 0 && _zoom <= min * 1.001f)
        {
            OnZoomPastMin?.Invoke(NearestCell(_camera));
            args.Handle();
            return;
        }

        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, args.Delta.Y), min, MaxZoom);

        // the point under the cursor stays under it
        var offset = (pixel - Middle) / _zoom;
        _camera = before - new Vector2(offset.X, -offset.Y);
        args.Handle();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _pressed = false;
            if (Drawing)
                OnToolCancelled?.Invoke();

            args.Handle();
            return;
        }

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _pressed = true;
        _dragging = false;
        _pressPos = args.RelativePixelPosition;
        _lastPos = _pressPos;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || !_pressed)
            return;

        _pressed = false;
        if (Drawing)
            ReleaseTool(args.RelativePixelPosition);
        else if (!_dragging && NearestCell(ScreenToWorld(args.RelativePixelPosition)) is { } cell)
            OnCellClicked?.Invoke(cell);

        _dragging = false;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var pos = args.RelativePixelPosition;
        var hovered = NearestCell(ScreenToWorld(pos));
        if (hovered != _hovered)
        {
            _hovered = hovered;
            OnCellHovered?.Invoke(hovered);
        }

        if (!_pressed)
            return;

        if ((pos - _pressPos).Length() > DragThreshold)
            _dragging = true;

        // without a tool the button drags the map
        if (_dragging && !Drawing)
        {
            var delta = (pos - _lastPos) / _zoom;
            _camera -= new Vector2(delta.X, -delta.Y);
        }

        _lastPos = pos;
    }

    private void ReleaseTool(Vector2 pixel)
    {
        if (Eraser)
        {
            if (MarkAt(pixel) is { } index)
                OnMarkErased?.Invoke(index);
            return;
        }

        if (Tool is not { } kind || CellAt(ScreenToWorld(_pressPos)) is not { } cell)
            return;

        var from = ToLocal(cell, ScreenToWorld(_pressPos));
        if (kind is not (ST14WarMarkKind.Attack or ST14WarMarkKind.Defend))
        {
            OnMarkPlaced?.Invoke(new ST14BattleMark(kind, cell, from, from));
            return;
        }

        var to = ToLocal(cell, ScreenToWorld(pixel));
        if (_dragging && (to - from).Length() >= 1f)
            OnMarkPlaced?.Invoke(new ST14BattleMark(kind, cell, from, to));
    }

    /// <summary>
    /// Index of the mark under the cursor for the eraser, an arrow is hit anywhere along it
    /// </summary>
    private int? MarkAt(Vector2 pixel)
    {
        if (_system.State is not { } state)
            return null;

        int? best = null;
        var bestDistance = 14f;
        for (var i = 0; i < state.Marks.Length; i++)
        {
            var mark = state.Marks[i];
            if (ToWorld(mark.Cell, mark.From) is not { } from || ToWorld(mark.Cell, mark.To) is not { } to)
                continue;

            var distance = DistanceToSegment(pixel, ToScreen(from), ToScreen(to));
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var line = b - a;
        var t = line.LengthSquared() < 0.001f ? 0 : Math.Clamp(Vector2.Dot(point - a, line) / line.LengthSquared(), 0, 1);
        return (point - (a + line * t)).Length();
    }

    #endregion

    #region Drawing

    private Vector2 Corner(Vector2 center, int corner)
    {
        var angle = MathF.PI / 6 + MathF.PI / 3 * corner;
        return ToScreen(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ST14SectorGeometry.Radius);
    }

    private void AddHex(Vector2 center, Color color)
    {
        var middle = ToScreen(center);
        for (var i = 0; i < 6; i++)
        {
            _buffer.Triangle(middle, Corner(center, i), Corner(center, i + 1), color);
        }
    }

    private void AddHexOutline(Vector2 center, float width, Color color)
    {
        for (var i = 0; i < 6; i++)
        {
            _buffer.Line(Corner(center, i), Corner(center, i + 1), width, color);
            _buffer.Circle(Corner(center, i), width / 2, color, 8);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(new UIBox2(Vector2.Zero, PixelSize), Back);
        if (_system.State is not { Active: true } state)
            return;

        UpdateLayout(state);
        if (!_framed && _layout.Count > 0)
            FrameBattle();

        ApplyFocus();

        // the ground first, each sector turned to its place
        _buffer.Clear();
        foreach (var (cell, place) in _layout)
        {
            var sector = Sector(cell);
            AddHex(place.Position, Unknown);
            if (sector is not { Loaded: true })
                AddHex(place.Position, ST14GalaxyColors.Of(sector?.Faction ?? ST14GalaxyFaction.Contested).WithAlpha(0.07f));
        }

        _buffer.Draw(handle, Texture.White);
        DrawTerrain(handle);

        _buffer.Clear();
        foreach (var (cell, place) in _layout)
        {
            if (Sector(cell) is { } sector)
                AddHexOutline(place.Position, sector.Loaded ? MathF.Max(2f, _zoom * 0.8f) : 1.5f, ST14GalaxyColors.Of(sector.Faction).WithAlpha(sector.Loaded ? 1f : 0.5f));
        }

        AddPassages();
        AddMarks(state);
        _buffer.Draw(handle, Texture.White);

        // things under the people, the people on top
        DrawIcons(handle);
        _buffer.Clear();
        AddMates();
        AddPlayer(state);
        _buffer.Draw(handle, Texture.White);
        DrawLabels(handle, state);
    }

    private void DrawTerrain(DrawingHandleScreen handle)
    {
        var old = handle.GetTransform();
        foreach (var (cell, place) in _layout)
        {
            if (Sector(cell) is not { Loaded: true } || _system.Terrain(cell) is not { } terrain)
                continue;

            var middle = ToScreen(place.Position);
            var size = new Vector2(ST14SectorGeometry.Size) * _zoom;

            // the screen goes down where the map goes up, so a turn of the map is the other way round on screen
            var turn = Matrix3x2.CreateTranslation(-middle) * Matrix3x2.CreateRotation(-MathF.PI / 3 * place.Turn) * Matrix3x2.CreateTranslation(middle);
            handle.SetTransform(turn * old);
            handle.DrawTextureRect(terrain, UIBox2.FromDimensions(middle - size / 2, size));
        }

        handle.SetTransform(old);
    }

    private void AddPassages()
    {
        var size = MathF.Max(5f, 9f * _zoom);
        foreach (var (cell, place) in _layout)
        {
            if (Sector(cell) is not { Loaded: true } sector)
                continue;

            for (var edge = 0; edge < 6; edge++)
            {
                var normal = ST14SectorGeometry.EdgeNormal((edge + place.Turn) % 6);
                var at = ToScreen(place.Position + normal * ST14SectorGeometry.Apothem);
                var direction = new Vector2(normal.X, -normal.Y);
                var side = new Vector2(-direction.Y, direction.X);

                // a pentagon has one edge with nothing behind it
                var open = edge < sector.Edges.Length && Sector(sector.Edges[edge]) is { Loaded: true };
                _buffer.Triangle(at + direction * size * 1.25f, at - direction * 2 - side * size * 1.15f, at - direction * 2 + side * size * 1.15f, Color.Black);
                _buffer.Triangle(at + direction * size, at - side * size * 0.9f, at + side * size * 0.9f, open ? Open : Closed);
            }
        }
    }

    private void AddMarks(ST14BattleMapStateEvent state)
    {
        var width = Math.Clamp(_zoom * 1.4f, 2.5f, 7f);
        foreach (var mark in state.Marks)
        {
            AddMark(state.Side, mark, width, 1f);
        }

        // the arrow being drawn follows the cursor
        if (_pressed && _dragging && Tool is { } tool and (ST14WarMarkKind.Attack or ST14WarMarkKind.Defend) &&
            CellAt(ScreenToWorld(_pressPos)) is { } cell)
        {
            AddMark(state.Side, new ST14BattleMark(tool, cell, ToLocal(cell, ScreenToWorld(_pressPos)), ToLocal(cell, ScreenToWorld(_lastPos))), width, 0.55f);
        }
    }

    private void AddMark(ST14GalaxyFaction side, ST14BattleMark mark, float width, float alpha)
    {
        if (ToWorld(mark.Cell, mark.From) is not { } fromWorld || ToWorld(mark.Cell, mark.To) is not { } toWorld)
            return;

        var from = ToScreen(fromWorld);
        var to = ToScreen(toWorld);
        foreach (var (grow, color) in new[]
                 {
                     // the dark edge keeps a mark readable on any ground
                     (MathF.Max(1.5f, width * 0.35f), Color.Black.WithAlpha(0.8f * alpha)),
                     (0f, ST14GalaxyMapView.MarkColor(side, mark.Kind).WithAlpha(alpha)),
                 })
        {
            if (!mark.IsArrow)
            {
                ST14MarkShapes.Sign(_buffer, mark.Kind, from, width, grow, color);
                continue;
            }

            if ((to - from).LengthSquared() < 0.01f)
                continue;

            _buffer.Line(from, to, width + grow * 2, color);
            ST14MarkShapes.Head(_buffer, to, Vector2.Normalize(to - from), width, grow, color);
        }
    }

    private float DotSize => Math.Clamp(_zoom * 2.2f, 4f, 12f);

    private void AddMates()
    {
        if (_system.Live is not { } live)
            return;

        foreach (var mate in live.Mates)
        {
            if (mate.Me || ToWorld(mate.Cell, mate.Position) is not { } world)
                continue;

            var at = ToScreen(world);
            _buffer.Circle(at, DotSize * 0.95f, Color.Black, 16);
            _buffer.Circle(at, DotSize * 0.7f, mate.Leader ? ST14HubStyle.GoldLight : Mate, 16);
        }
    }

    private void AddPlayer(ST14BattleMapStateEvent state)
    {
        if (state.Here < 0 || _system.OwnPosition() is not { } position || ToWorld(state.Here, position) is not { } world)
            return;

        var at = ToScreen(world);
        var size = DotSize;
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 4f);
        var ring = Color.White.WithAlpha(0.7f - 0.5f * pulse);
        _buffer.Ring(at, size * (1.2f + pulse), size * (1.5f + pulse), ring, ring, 24);
        _buffer.Circle(at, size * 0.85f, Color.Black, 16);
        _buffer.Circle(at, size * 0.6f, Color.White, 16);
    }

    private void DrawIcons(DrawingHandleScreen handle)
    {
        if (_system.Live is not { } live)
            return;

        var size = Math.Clamp(_zoom * 7f, 22f, 44f);
        foreach (var icon in live.Icons)
        {
            if (ToWorld(icon.Cell, icon.Position) is not { } world)
                continue;

            var at = ToScreen(world);
            handle.DrawCircle(at, size * 0.62f, Color.Black.WithAlpha(0.7f));
            handle.DrawCircle(at, size * 0.62f, ST14HubStyle.GoldLight, false);
            var texture = _sprite.GetPrototypeIcon(icon.Prototype).Default;
            handle.DrawTextureRect(texture, UIBox2.FromDimensions(at - new Vector2(size / 2), new Vector2(size)));
        }
    }

    private void DrawLabels(DrawingHandleScreen handle, ST14BattleMapStateEvent state)
    {
        var hexPixels = ST14SectorGeometry.Radius * _zoom;
        var codeFont = ST14HubStyle.Font(_cache, ST14HubFont.SemiBold, Math.Clamp((int) (hexPixels * 0.16f), 10, 26));
        var tagFont = ST14HubStyle.Font(_cache, ST14HubFont.Medium, Math.Clamp((int) (hexPixels * 0.09f), 9, 16));
        foreach (var (cell, place) in _layout)
        {
            if (Sector(cell) is not { } sector)
                continue;

            // close up the code moves to the top of the sector, out of the way of the ground
            var at = ToScreen(place.Position + new Vector2(0, hexPixels > 220 ? ST14SectorGeometry.Apothem * 0.82f : 0));
            ST14HubStyle.DrawRotatedText(handle, codeFont, sector.Code, at, 0, sector.Loaded ? Color.White : Color.White.WithAlpha(0.6f), ST14HubStyle.TextShadow);

            var tag = cell == state.Here ? "st14-battle-map-tag-here"
                : cell == state.Target ? "st14-battle-map-tag-target"
                : cell == state.Staging ? "st14-battle-map-tag-staging"
                : null;
            if (tag != null)
                ST14HubStyle.DrawRotatedText(handle, tagFont, Loc.GetString(tag).ToUpperInvariant(), at + new Vector2(0, codeFont.GetLineHeight(1f)), 0, ST14HubStyle.GoldLight, ST14HubStyle.TextShadow);
        }

        var nameFont = ST14HubStyle.Font(_cache, ST14HubFont.Medium, 12);
        if (_system.Live is { } live)
        {
            foreach (var mate in live.Mates)
            {
                if (!mate.Me && ToWorld(mate.Cell, mate.Position) is { } world)
                    ST14HubStyle.DrawRotatedText(handle, nameFont, mate.Name, ToScreen(world) + new Vector2(0, -DotSize * 2), 0, Color.White, ST14HubStyle.TextShadow);
            }
        }

        if (state.Here >= 0 && _system.OwnPosition() is { } position && ToWorld(state.Here, position) is { } own)
        {
            ST14HubStyle.DrawRotatedText(handle, nameFont, Loc.GetString("st14-battle-map-you").ToUpperInvariant(),
                ToScreen(own) + new Vector2(0, -DotSize * 2.4f), 0, Color.White, ST14HubStyle.TextShadow);
        }
    }

    #endregion
}
