using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// One sector drawn flat with its ground and the passages on its edges, the sectors behind them as plates around it. The
/// planet is a sphere, so neighbours do not fit one flat map: a plate opens its sector here instead. A command draws its
/// marks with the left button, the right button puts the tool away
/// </summary>
public sealed class ST14BattleMapView : Control
{
    private const float DragThreshold = 6f;

    private static readonly Color Back = Color.FromHex("#0C0D0B");
    private static readonly Color Unknown = Color.FromHex("#26241F");
    private static readonly Color Open = Color.FromHex("#6FE08A");
    private static readonly Color Closed = Color.FromHex("#A8A49A");

    private readonly IResourceCache _cache;
    private readonly ST14BattleMapClientSystem _system;
    private readonly ST14VertexBuffer _buffer = new();

    private float _time;
    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;
    private Vector2 _lastPos;
    private int? _hoveredPlate;

    /// <summary>
    /// The sector shown in the middle
    /// </summary>
    public int ViewCell = -1;

    public ST14WarMarkKind? Tool;
    public bool Eraser;

    public event Action<int>? OnCellClicked;
    public event Action<ST14BattleMark>? OnMarkPlaced;
    public event Action<int>? OnMarkErased;
    public event Action? OnToolCancelled;

    public ST14BattleMapView(IResourceCache cache, ST14BattleMapClientSystem system)
    {
        _cache = cache;
        _system = system;
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;
    }

    private bool Drawing => Tool != null || Eraser;

    private ST14BattleSector? Sector(int cell)
    {
        return _system.State?.Sectors.FirstOrDefault(s => s.Cell == cell) is { Code: not null } sector ? sector : null;
    }

    private Vector2 Middle => (Vector2) PixelSize / 2;

    /// <summary>
    /// Pixels from the middle of the view to a corner of the sector
    /// </summary>
    private float HexRadius => MathF.Min(PixelSize.X, PixelSize.Y) / 2 * 0.74f;

    private float PlateRadius => HexRadius * 0.17f;

    private float Scale => HexRadius / ST14SectorGeometry.Radius;

    private Vector2 ToScreen(Vector2 tile)
    {
        var offset = (tile - ST14SectorGeometry.Center) * Scale;
        return Middle + new Vector2(offset.X, -offset.Y);
    }

    private Vector2 ToTile(Vector2 pixel)
    {
        var offset = (pixel - Middle) / Scale;
        return ST14SectorGeometry.Center + new Vector2(offset.X, -offset.Y);
    }

    private Vector2 PlateCenter(int edge)
    {
        var normal = ST14SectorGeometry.EdgeNormal(edge);
        var distance = HexRadius * MathF.Cos(MathF.PI / 6) + PlateRadius * 1.5f;
        return Middle + new Vector2(normal.X, -normal.Y) * distance;
    }

    private int? PlateAt(Vector2 pixel)
    {
        if (Sector(ViewCell) is not { } sector)
            return null;

        for (var edge = 0; edge < sector.Edges.Length; edge++)
        {
            if ((pixel - PlateCenter(edge)).Length() <= PlateRadius)
                return sector.Edges[edge];
        }

        return null;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
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
        var pixel = args.RelativePixelPosition;
        if (!_dragging && PlateAt(pixel) is { } cell)
        {
            OnCellClicked?.Invoke(cell);
            args.Handle();
            return;
        }

        if (Drawing)
            ReleaseTool(pixel);

        _dragging = false;
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        var pos = args.RelativePixelPosition;
        _hoveredPlate = PlateAt(pos);
        if (!_pressed)
            return;

        if ((pos - _pressPos).Length() > DragThreshold)
            _dragging = true;

        _lastPos = pos;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hoveredPlate = null;
    }

    private void ReleaseTool(Vector2 pixel)
    {
        if (Sector(ViewCell) is not { Loaded: true })
            return;

        if (Eraser)
        {
            if (MarkAt(pixel) is { } index)
                OnMarkErased?.Invoke(index);
            return;
        }

        if (Tool is not { } kind)
            return;

        var from = ToTile(_pressPos);
        var to = ToTile(pixel);
        if (!ST14SectorGeometry.Contains(from))
            return;

        if (kind is not (ST14WarMarkKind.Attack or ST14WarMarkKind.Defend))
        {
            OnMarkPlaced?.Invoke(new ST14BattleMark(kind, ViewCell, from, from));
            return;
        }

        if (_dragging && (to - from).Length() >= 1f)
            OnMarkPlaced?.Invoke(new ST14BattleMark(kind, ViewCell, from, to));
    }

    /// <summary>
    /// Index of the mark under the cursor for the eraser, an arrow is hit anywhere along it
    /// </summary>
    private int? MarkAt(Vector2 pixel)
    {
        if (_system.State is not { } state)
            return null;

        int? best = null;
        var bestDistance = HexRadius * 0.08f;
        for (var i = 0; i < state.Marks.Length; i++)
        {
            var mark = state.Marks[i];
            if (mark.Cell != ViewCell)
                continue;

            var distance = DistanceToSegment(pixel, ToScreen(mark.From), ToScreen(mark.To));
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

    private void AddHex(Vector2 center, float radius, float rotation, Color color)
    {
        for (var i = 0; i < 6; i++)
        {
            var a = rotation + MathF.PI / 3 * i;
            var b = a + MathF.PI / 3;
            _buffer.Triangle(center, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, color);
        }
    }

    private void AddHexOutline(Vector2 center, float radius, float width, Color color)
    {
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.PI / 6 + MathF.PI / 3 * i;
            var b = a + MathF.PI / 3;
            var from = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            _buffer.Line(from, center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, width, color);
            _buffer.Circle(from, width / 2, color, 8);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(new UIBox2(Vector2.Zero, PixelSize), Back);
        if (_system.State is not { Active: true } state || Sector(ViewCell) is not { } sector)
            return;

        _buffer.Clear();
        AddHex(Middle, HexRadius, MathF.PI / 6, Unknown);
        _buffer.Draw(handle, Texture.White);

        if (sector.Loaded && _system.Terrain(ViewCell) is { } terrain)
        {
            var corner = ToScreen(new Vector2(0, ST14SectorGeometry.Size));
            var size = new Vector2(ST14SectorGeometry.Size) * Scale;
            handle.DrawTextureRect(terrain, UIBox2.FromDimensions(corner, size));
        }

        _buffer.Clear();
        AddHexOutline(Middle, HexRadius, MathF.Max(2f, HexRadius / 90f), ST14GalaxyColors.Of(sector.Faction));
        AddPassages(sector);
        AddMarks(state);
        AddPlayer(state);
        _buffer.Draw(handle, Texture.White);

        DrawPlateTexts(handle, state, sector);
        DrawYou(handle, state);
    }

    private void AddPassages(ST14BattleSector sector)
    {
        var size = HexRadius * 0.07f;
        for (var edge = 0; edge < 6; edge++)
        {
            var normal = ST14SectorGeometry.EdgeNormal(edge);
            var direction = new Vector2(normal.X, -normal.Y);
            var side = new Vector2(-direction.Y, direction.X);
            var at = ToScreen(ST14SectorGeometry.Center + normal * ST14SectorGeometry.Apothem);

            // a pentagon has one edge with nothing behind it
            var neighbour = edge < sector.Edges.Length ? Sector(sector.Edges[edge]) : null;
            var open = sector.Loaded && neighbour is { Loaded: true };
            var color = open ? Open : Closed;

            _buffer.Triangle(at + direction * size * 1.25f, at - direction * 2 - side * size * 1.15f, at - direction * 2 + side * size * 1.15f, Color.Black);
            _buffer.Triangle(at + direction * size, at - side * size * 0.9f, at + side * size * 0.9f, color);

            if (neighbour is not { } next)
                continue;

            var plate = PlateCenter(edge);
            _buffer.Line(at + direction * size, plate - direction * PlateRadius, MathF.Max(2f, size * 0.1f), color.WithAlpha(0.8f));

            var hot = _hoveredPlate == next.Cell;
            var fill = ST14GalaxyColors.Of(next.Faction).WithAlpha(next.Loaded ? hot ? 0.75f : 0.5f : 0.18f);
            AddHex(plate, PlateRadius, MathF.PI / 6, Back);
            AddHex(plate, PlateRadius, MathF.PI / 6, fill);
            AddHexOutline(plate, PlateRadius, hot ? 2.5f : 1.5f, hot ? Color.White : color);
        }
    }

    private void AddMarks(ST14BattleMapStateEvent state)
    {
        var width = MathF.Max(3f, HexRadius / 70f);
        foreach (var mark in state.Marks)
        {
            if (mark.Cell == ViewCell)
                AddMark(state.Side, mark, width, 1f);
        }

        // the arrow being drawn follows the cursor
        if (_pressed && _dragging && Tool is { } tool and (ST14WarMarkKind.Attack or ST14WarMarkKind.Defend))
            AddMark(state.Side, new ST14BattleMark(tool, ViewCell, ToTile(_pressPos), ToTile(_lastPos)), width, 0.55f);
    }

    private void AddMark(ST14GalaxyFaction side, ST14BattleMark mark, float width, float alpha)
    {
        // the dark edge keeps a mark readable on any ground
        AddMarkShape(mark, width, MathF.Max(1.5f, width * 0.35f), Color.Black.WithAlpha(0.8f * alpha));
        AddMarkShape(mark, width, 0, ST14GalaxyMapView.MarkColor(side, mark.Kind).WithAlpha(alpha));
    }

    private void AddMarkShape(ST14BattleMark mark, float width, float grow, Color color)
    {
        var from = ToScreen(mark.From);
        if (!mark.IsArrow)
        {
            ST14MarkShapes.Sign(_buffer, mark.Kind, from, width, grow, color);
            return;
        }

        var to = ToScreen(mark.To);
        if ((to - from).LengthSquared() < 0.01f)
            return;

        _buffer.Line(from, to, width + grow * 2, color);
        ST14MarkShapes.Head(_buffer, to, Vector2.Normalize(to - from), width, grow, color);
    }

    private void AddPlayer(ST14BattleMapStateEvent state)
    {
        if (state.Here != ViewCell || _system.OwnPosition() is not { } position)
            return;

        var at = ToScreen(position);
        var size = MathF.Max(5f, HexRadius / 45f);
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 4f);
        _buffer.Ring(at, size * (1.2f + pulse), size * (1.5f + pulse), Color.White.WithAlpha(0.7f - 0.5f * pulse), Color.White.WithAlpha(0.7f - 0.5f * pulse), 24);
        _buffer.Circle(at, size * 0.85f, Color.Black, 16);
        _buffer.Circle(at, size * 0.6f, Color.White, 16);
    }

    private void DrawPlateTexts(DrawingHandleScreen handle, ST14BattleMapStateEvent state, ST14BattleSector sector)
    {
        var font = ST14HubStyle.Font(_cache, ST14HubFont.Medium, Math.Max(9, (int) (PlateRadius * 0.32f)));
        for (var edge = 0; edge < sector.Edges.Length; edge++)
        {
            if (Sector(sector.Edges[edge]) is not { } next)
                continue;

            var center = PlateCenter(edge);
            ST14HubStyle.DrawRotatedText(handle, font, next.Code, center, 0, Color.White, ST14HubStyle.TextShadow);

            var tag = next.Cell == state.Here ? "st14-battle-map-tag-here"
                : next.Cell == state.Target ? "st14-battle-map-tag-target"
                : next.Cell == state.Staging ? "st14-battle-map-tag-staging"
                : null;
            if (tag != null)
                ST14HubStyle.DrawRotatedText(handle, font, Loc.GetString(tag).ToUpperInvariant(), center + new Vector2(0, PlateRadius * 0.55f), 0, ST14HubStyle.GoldLight, ST14HubStyle.TextShadow);
        }
    }

    private void DrawYou(DrawingHandleScreen handle, ST14BattleMapStateEvent state)
    {
        if (state.Here != ViewCell || _system.OwnPosition() is not { } position)
            return;

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Medium, Math.Max(10, (int) (HexRadius / 28f)));
        ST14HubStyle.DrawRotatedText(handle, font, Loc.GetString("st14-battle-map-you").ToUpperInvariant(),
            ToScreen(position) + new Vector2(0, -HexRadius * 0.07f), 0, Color.White, ST14HubStyle.TextShadow);
    }
}
