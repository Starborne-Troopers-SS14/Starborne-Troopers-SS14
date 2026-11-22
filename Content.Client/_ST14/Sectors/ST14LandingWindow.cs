using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// Where to come into the battle: the region laid flat, the sectors in the colours of their holders, the landing points
/// lit on it. A point is picked on the map or in the list under it; the usual landing is always there too
/// </summary>
public sealed class ST14LandingWindow : DefaultWindow
{
    public event Action<NetEntity?>? OnPicked;

    public ST14LandingWindow(ST14LandingOptionsEvent options)
    {
        Title = Loc.GetString("st14-landing-title");
        MinSize = new Vector2(560, 620);

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        Contents.AddChild(column);

        column.AddChild(new Label { Text = Loc.GetString("st14-landing-hint"), FontColorOverride = Color.LightGray });

        var map = new ST14LandingMap(options) { VerticalExpand = true, HorizontalExpand = true, MinSize = new Vector2(520, 380) };
        map.OnPicked += point => OnPicked?.Invoke(point);
        column.AddChild(map);

        var usual = new Button { Text = Loc.GetString("st14-landing-usual"), MinHeight = 34 };
        usual.OnPressed += _ => OnPicked?.Invoke(null);
        column.AddChild(usual);

        foreach (var point in options.Points.OrderBy(p => p.Code))
        {
            var button = new Button
            {
                Text = Loc.GetString("st14-landing-base", ("code", point.Code), ("supplies", point.Supplies)),
                MinHeight = 30,
            };
            var picked = point.Base;
            button.OnPressed += _ => OnPicked?.Invoke(picked);
            column.AddChild(button);
        }
    }
}

/// <summary>
/// The region of the battle laid flat around its centre, with the landing points in the sectors they stand in
/// </summary>
public sealed class ST14LandingMap : Control
{
    private const float PointRadius = 7f;

    public event Action<NetEntity>? OnPicked;

    private readonly ST14LandingOptionsEvent _options;
    private readonly ST14PlanetWar? _war;
    private readonly Dictionary<int, Vector2> _flat = new();
    private float _spacing = 1f;
    private NetEntity? _hovered;

    public ST14LandingMap(ST14LandingOptionsEvent options)
    {
        _options = options;
        MouseFilter = MouseFilterMode.Stop;

        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var entities = IoCManager.Resolve<IEntityManager>();
        if (!prototypes.TryIndex<ST14GalaxyPlanetPrototype>(options.Planet, out var planet))
            return;

        var name = Loc.GetString(planet.Name);
        _war = new ST14PlanetWar(planet, name.Length > 0 ? name[..1].ToUpperInvariant() : "X");
        if (entities.System<ST14GalaxyWarClientSystem>().TryGetState(planet.ID, out var state))
            _war.ImportState(state.Cells, state.Turn, state.ActiveRegion);

        Lay();
    }

    /// <summary>
    /// The cells of the region on the plane that touches the planet at the centre of the region: east to the right,
    /// north up, as the sector maps lie
    /// </summary>
    private void Lay()
    {
        if (_war == null)
            return;

        var cells = Enumerable.Range(0, _war.Cells.Length)
            .Where(i => _options.Region < 0 ? _options.Points.Any(p => p.Cell == i) : _war.Cells[i].Region == _options.Region)
            .ToList();

        // the cells with landing points always show, even outside the region
        foreach (var point in _options.Points)
        {
            if (!cells.Contains(point.Cell))
                cells.Add(point.Cell);
        }

        if (cells.Count == 0)
            return;

        var centre = Vector3.Normalize(cells.Aggregate(Vector3.Zero, (sum, i) => sum + _war.Grid.Centers[i]));
        var east = Vector3.Cross(Vector3.UnitY, centre);
        east = east.LengthSquared() > 0.0001f ? Vector3.Normalize(east) : Vector3.UnitX;
        var north = Vector3.Cross(centre, east);

        foreach (var cell in cells)
        {
            var c = _war.Grid.Centers[cell];
            _flat[cell] = new Vector2(Vector3.Dot(c, east), Vector3.Dot(c, north));
        }

        var first = cells[0];
        var neighbour = _war.Grid.Neighbours[first][0];
        var a = _war.Grid.Centers[first];
        var b = _war.Grid.Centers[neighbour];
        _spacing = MathF.Max(0.0001f, (new Vector2(Vector3.Dot(a, east), Vector3.Dot(a, north)) -
                                       new Vector2(Vector3.Dot(b, east), Vector3.Dot(b, north))).Length());
    }

    /// <summary>
    /// From the plane to pixels of the control, the region fitted in with a margin
    /// </summary>
    private (Vector2 Offset, float Scale) Fit()
    {
        if (_flat.Count == 0)
            return (Vector2.Zero, 1f);

        var min = new Vector2(_flat.Values.Min(p => p.X), _flat.Values.Min(p => p.Y)) - new Vector2(_spacing);
        var max = new Vector2(_flat.Values.Max(p => p.X), _flat.Values.Max(p => p.Y)) + new Vector2(_spacing);
        var size = PixelSize;
        var scale = MathF.Min(size.X / (max.X - min.X), size.Y / (max.Y - min.Y));
        var offset = new Vector2(size.X / 2f, size.Y / 2f) - (min + max) / 2f * new Vector2(scale, -scale);
        return (offset, scale);
    }

    private Vector2 ToPixel(Vector2 flat, (Vector2 Offset, float Scale) fit)
    {
        return fit.Offset + new Vector2(flat.X, -flat.Y) * fit.Scale;
    }

    /// <summary>
    /// A landing point on the screen: in its sector, placed by where it stands in the sector's tiles
    /// </summary>
    private Vector2? PointPixel(ST14LandingPoint point, (Vector2 Offset, float Scale) fit)
    {
        if (!_flat.TryGetValue(point.Cell, out var centre))
            return null;

        var hexRadius = _spacing * 0.55f * fit.Scale;
        var inSector = (point.Position - ST14SectorGeometry.Center) / ST14SectorGeometry.Radius;
        return ToPixel(centre, fit) + new Vector2(inSector.X, -inSector.Y) * hexRadius;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, new Color(0.05f, 0.07f, 0.09f));
        if (_war == null)
            return;

        var fit = Fit();
        var radius = _spacing * 0.55f * fit.Scale;
        var corners = new Vector2[7];
        foreach (var (cell, flat) in _flat)
        {
            var centre = ToPixel(flat, fit);
            for (var i = 0; i < 7; i++)
            {
                var angle = MathF.PI / 6 + MathF.PI / 3 * i;
                corners[i] = centre + new Vector2(MathF.Cos(angle), -MathF.Sin(angle)) * radius;
            }

            var colour = ST14GalaxyColors.Of(_war.Cells[cell].Faction);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, corners.AsSpan(0, 6), colour.WithAlpha(0.22f));
            handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, corners, colour.WithAlpha(0.8f));
        }

        foreach (var point in _options.Points)
        {
            if (PointPixel(point, fit) is not { } at)
                continue;

            var lit = _hovered == point.Base;
            handle.DrawCircle(at, PointRadius + (lit ? 4 : 2), Color.Black.WithAlpha(0.6f));
            handle.DrawCircle(at, PointRadius, lit ? Color.White : ST14GalaxyColors.Contested);
        }
    }

    private NetEntity? PointAt(Vector2 pixel)
    {
        var fit = Fit();
        foreach (var point in _options.Points)
        {
            if (PointPixel(point, fit) is { } at && (at - pixel).Length() <= PointRadius + 4)
                return point.Base;
        }

        return null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _hovered = PointAt(args.RelativePixelPosition);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || PointAt(args.RelativePixelPosition) is not { } point)
            return;

        args.Handle();
        OnPicked?.Invoke(point);
    }
}
