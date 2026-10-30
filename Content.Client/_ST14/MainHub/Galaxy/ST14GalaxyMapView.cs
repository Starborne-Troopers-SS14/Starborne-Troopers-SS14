using System.Linq;
using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// War table of the galactic war: the Milky Way seen from above with the galactic core in the centre, rings of sectors around it,
/// enemy space hatched, planets with supply lines on top. The camera pans with the mouse, zooms with the wheel
/// and flies to the sector or dives to the planet the screen asks for
/// </summary>
public sealed class ST14GalaxyMapView : Control
{
    private const float DragThreshold = 5f;
    private const float ArcStep = 2f;
    private const float MinZoom = 0.85f;
    private const float MaxZoom = 7f;
    private const float EdgeRadius = 0.97f;

    private static readonly Color LineColor = new(0.62f, 0.72f, 0.84f);
    private static readonly Color SupplyColor = Color.FromHex("#CFE6FF");
    private static readonly Color WarColor = Color.FromHex("#FFA22E");

    private readonly IResourceCache _cache;
    private readonly ST14GalaxyTextures _textures;
    private readonly ST14VertexBuffer _glow = new();
    private readonly ST14VertexBuffer _shapes = new();
    private readonly ST14VertexBuffer _hatch = new();
    private readonly List<(ST14GalaxySectorPrototype Sector, Vector2 Label, float Extent, string Name)> _sectors = new();
    private readonly List<(ST14GalaxyPlanetPrototype Planet, Vector2 Position, string Name)> _planets = new();
    private readonly List<(int From, int To)> _links = new();
    private readonly List<(float Angle, string Text)> _zoneLabels = new();

    private static readonly (Vector2 Pos, float Size, Color Color, float Phase)[] Stars;
    private static readonly (Vector2 Pos, float Radius, Color Color)[] Clouds;

    private Vector2 _cam;
    private Vector2 _camTarget;
    private float _zoom = 1;
    private float _zoomTarget = 1;
    private float _inset;
    private float _insetTarget;
    private float _time;

    private bool _pressed;
    private bool _dragging;
    private Vector2 _pressPos;
    private Vector2 _lastPos;

    private ST14GalaxySectorPrototype? _hoveredSector;
    private int? _hoveredPlanet;
    private ST14GalaxyPlanetPrototype? _diving;

    public ST14GalaxySectorPrototype? SelectedSector;

    public event Action<ST14GalaxySectorPrototype>? OnSectorClicked;
    public event Action<ST14GalaxyPlanetPrototype>? OnPlanetClicked;
    public event Action? OnEmptyClicked;
    public event Action? OnBackClicked;

    /// <summary>
    /// Plan of the High Command drawn over the map
    /// </summary>
    public IReadOnlyList<(ST14GalaxyFaction Side, int Index, ST14WarMark Mark)> Marks =
        Array.Empty<(ST14GalaxyFaction, int, ST14WarMark)>();

    /// <summary>
    /// Side whose plan the tools draw into and erase from
    /// </summary>
    public ST14GalaxyFaction ToolSide = ST14GalaxyFaction.Federation;

    /// <summary>
    /// Drawing tool of the High Command, null when the map is only looked at
    /// </summary>
    public ST14WarMarkKind? Tool;

    public bool Eraser;

    public event Action<ST14WarMark>? OnMarkPlaced;
    public event Action<ST14GalaxyFaction, int>? OnMarkErased;
    public event Action? OnToolCancelled;

    private bool Drawing => Tool != null || Eraser;

    private bool DrawsArrow => Tool is ST14WarMarkKind.Attack or ST14WarMarkKind.Defend;

    static ST14GalaxyMapView()
    {
        var random = new Random(1984);
        float Gauss()
        {
            var u = 1 - random.NextSingle();
            var v = random.NextSingle();
            return MathF.Sqrt(-2 * MathF.Log(u)) * MathF.Cos(MathF.Tau * v);
        }

        // the bar of the Milky Way lies at this angle, the arms start at its ends
        var bar = 25f * MathF.PI / 180f;
        var pitch = MathF.Tan(22f * MathF.PI / 180f);
        var armWeights = new[] { 0.32f, 0.18f, 0.32f, 0.18f };

        Vector2 ArmPoint(int arm, float r, float spread)
        {
            var theta = bar + arm * MathF.PI / 2 + MathF.Log(r / 0.12f) / pitch;
            var along = new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * r;
            return along + new Vector2(Gauss(), Gauss()) * spread;
        }

        int PickArm()
        {
            var roll = random.NextSingle();
            for (var i = 0; i < armWeights.Length; i++)
            {
                roll -= armWeights[i];
                if (roll <= 0)
                    return i;
            }

            return 0;
        }

        // the Milky Way itself: bulge and bar, four spiral arms and a thin disc between them
        var stars = new List<(Vector2, float, Color, float)>();
        var warm = new Color(1f, 0.86f, 0.62f);
        var blue = new Color(0.76f, 0.85f, 1f);
        var pink = new Color(1f, 0.6f, 0.78f);

        for (var i = 0; i < 1400; i++)
        {
            var p = new Vector2(Gauss() * 0.13f, Gauss() * 0.055f);
            p = Vector2.Transform(p, Matrix3x2.CreateRotation(bar));
            stars.Add((p, 0.6f + random.NextSingle() * 1.1f, Color.InterpolateBetween(warm, Color.White, random.NextSingle() * 0.4f),
                random.NextSingle() * MathF.Tau));
        }

        for (var i = 0; i < 5600; i++)
        {
            var r = 0.12f + 0.86f * MathF.Sqrt(random.NextSingle());
            var p = ArmPoint(PickArm(), r, 0.022f + 0.04f * r);
            var roll = random.NextSingle();
            var color = roll < 0.04f ? pink : roll < 0.25f ? Color.InterpolateBetween(warm, Color.White, 0.5f) : blue;
            stars.Add((p, 0.5f + random.NextSingle() * random.NextSingle() * 1.6f, color, random.NextSingle() * MathF.Tau));
        }

        for (var i = 0; i < 1800; i++)
        {
            var r = MathF.Sqrt(random.NextSingle()) * 0.98f;
            var a = random.NextSingle() * MathF.Tau;
            stars.Add((new Vector2(MathF.Cos(a), MathF.Sin(a)) * r, 0.4f + random.NextSingle() * 0.6f,
                Color.InterpolateBetween(blue, warm, random.NextSingle()).WithAlpha(0.6f), random.NextSingle() * MathF.Tau));
        }

        // colours are converted to linear once here, the stars are redrawn every frame
        Stars = stars.Select(s => (s.Item1, s.Item2, Color.FromSrgb(s.Item3), s.Item4)).ToArray();

        var clouds = new List<(Vector2, float, Color)>
        {
            (Vector2.Zero, 1.05f, new Color(0.35f, 0.45f, 0.65f, 0.1f)),
            (Vector2.Zero, 0.36f, new Color(1f, 0.78f, 0.5f, 0.45f)),
            (Vector2.Zero, 0.17f, new Color(1f, 0.9f, 0.72f, 0.7f)),
        };

        for (var i = 0; i < 300; i++)
        {
            var r = 0.14f + 0.8f * MathF.Sqrt(random.NextSingle());
            clouds.Add((ArmPoint(PickArm(), r, 0.02f), 0.05f + random.NextSingle() * 0.08f,
                new Color(0.45f, 0.6f, 0.95f, 0.05f + random.NextSingle() * 0.05f)));
        }

        for (var i = 0; i < 40; i++)
        {
            var r = 0.2f + 0.7f * random.NextSingle();
            clouds.Add((ArmPoint(PickArm(), r, 0.015f), 0.015f + random.NextSingle() * 0.02f, new Color(1f, 0.45f, 0.65f, 0.22f)));
        }

        Clouds = clouds.Select(c => (c.Item1, c.Item2, Color.FromSrgb(c.Item3))).ToArray();
    }

    public ST14GalaxyMapView(IPrototypeManager prototypes, IResourceCache cache, ST14GalaxyTextures textures)
    {
        _cache = cache;
        _textures = textures;
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;

        foreach (var sector in prototypes.EnumeratePrototypes<ST14GalaxySectorPrototype>().OrderBy(s => s.Ring).ThenBy(s => s.Start))
        {
            var (inner, outer) = Radii(sector);
            var mid = Deg((sector.Start + sector.End) / 2);
            var radius = sector.Ring == 0 ? 0 : (inner + outer) / 2;
            var label = new Vector2(MathF.Cos(mid), MathF.Sin(mid)) * radius;
            var arc = Deg(sector.End - sector.Start) * radius;
            var extent = sector.Ring == 0 ? outer : MathF.Max(outer - inner, arc) / 2 * 1.15f;
            _sectors.Add((sector, label, extent, Loc.GetString(sector.Name).ToUpperInvariant()));
        }

        foreach (var planet in prototypes.EnumeratePrototypes<ST14GalaxyPlanetPrototype>().OrderBy(p => p.ID))
        {
            var angle = Deg(planet.Angle);
            _planets.Add((planet, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * planet.Radius, Loc.GetString(planet.Name).ToUpperInvariant()));
        }

        for (var i = 0; i < _planets.Count; i++)
        {
            foreach (var link in _planets[i].Planet.Links)
            {
                var j = _planets.FindIndex(p => p.Planet.ID == link);
                if (j >= 0)
                    _links.Add((i, j));
            }
        }

        // the name of the enemy runs along the rim over the middle of its outer sectors
        var outerRing = ST14GalaxySectorPrototype.RingRadius.Length - 1;
        var enemy = _sectors.Where(s => s.Sector.Ring == outerRing && s.Sector.Faction == ST14GalaxyFaction.Arachnid).ToList();
        if (enemy.Count > 0)
        {
            var mid = (enemy.Min(s => s.Sector.Start) + enemy.Max(s => s.Sector.End)) / 2;
            _zoneLabels.Add((mid, Loc.GetString("st14-galaxy-zone-arachnid").ToUpperInvariant()));
        }
    }

    public IEnumerable<ST14GalaxySectorPrototype> Sectors => _sectors.Select(s => s.Sector);

    public IEnumerable<ST14GalaxyPlanetPrototype> Planets => _planets.Select(p => p.Planet);

    /// <summary>
    /// Part of the width on the right covered by the info panel, the camera keeps its target left of it
    /// </summary>
    public float PanelInset
    {
        set => _insetTarget = value;
    }

    private static float Deg(float degrees)
    {
        return degrees * MathF.PI / 180f;
    }

    private static (float Inner, float Outer) Radii(ST14GalaxySectorPrototype sector)
    {
        var rings = ST14GalaxySectorPrototype.RingRadius;
        var ring = Math.Clamp(sector.Ring, 0, rings.Length - 1);
        return (ring == 0 ? 0 : rings[ring - 1], rings[ring]);
    }

    public void FocusGalaxy()
    {
        _diving = null;
        _camTarget = Vector2.Zero;
        _zoomTarget = 1;
    }

    public void FocusSector(ST14GalaxySectorPrototype sector)
    {
        _diving = null;
        var entry = _sectors.FirstOrDefault(s => s.Sector == sector);
        if (entry.Sector == null)
            return;

        _camTarget = entry.Label;
        _zoomTarget = Math.Clamp(0.55f / entry.Extent, 1.6f, 5.5f);
    }

    /// <summary>
    /// The camera falls onto the planet until it fills the view, the screen then swaps in the planet itself
    /// </summary>
    public void Dive(ST14GalaxyPlanetPrototype planet)
    {
        var entry = _planets.FirstOrDefault(p => p.Planet == planet);
        if (entry.Planet == null)
            return;

        _diving = planet;
        _camTarget = entry.Position;
        _zoomTarget = 22f;
    }

    private float UiScale => MathF.Min(PixelSize.X / ST14CanvasLayout.CanvasSize.X, PixelSize.Y / ST14CanvasLayout.CanvasSize.Y);

    private float BaseScale => MathF.Min(PixelSize.X, PixelSize.Y) * 0.47f;

    private float Scale => BaseScale * _zoom;

    private Vector2 ViewCenter => new(PixelSize.X * (1 - _inset) / 2, PixelSize.Y * 0.48f);

    private Vector2 ToScreen(Vector2 galaxy)
    {
        return ViewCenter + (galaxy - _cam) * Scale;
    }

    private Vector2 ToGalaxy(Vector2 screen)
    {
        return _cam + (screen - ViewCenter) / Scale;
    }

    private ST14HubSoundController Sounds => UserInterfaceManager.GetUIController<ST14HubSoundController>();

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree)
            return;

        var dt = args.DeltaSeconds;
        _time += dt;
        // a dive accelerates into the planet, everything else glides
        var k = 1 - MathF.Exp(-dt * (_diving != null ? 3.5f : 5f));
        _cam = Vector2.Lerp(_cam, _camTarget, MathF.Min(1, k * 1.6f));
        _zoom += (_zoomTarget - _zoom) * k;
        _inset += (_insetTarget - _inset) * k;
    }

    #region Input

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (_diving != null)
            return;

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            // the right button puts the drawing tool away first
            if (Drawing)
                OnToolCancelled?.Invoke();
            else
                OnBackClicked?.Invoke();

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
        {
            ReleaseTool(args.RelativePixelPosition);
            _dragging = false;
            args.Handle();
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            return;
        }

        UpdateHover(args.RelativePixelPosition);
        if (_hoveredPlanet is { } planet)
        {
            Sounds.Play(ST14HubSound.Click);
            OnPlanetClicked?.Invoke(_planets[planet].Planet);
        }
        else if (_hoveredSector != null)
        {
            Sounds.Play(ST14HubSound.Click);
            OnSectorClicked?.Invoke(_hoveredSector);
        }
        else
        {
            OnEmptyClicked?.Invoke();
        }

        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_diving != null)
            return;

        var pos = args.RelativePixelPosition;
        if (_pressed)
        {
            if (!_dragging && (pos - _pressPos).Length() > DragThreshold)
                _dragging = true;

            // an arrow is drawn with the drag, the other tools keep panning
            if (_dragging && !DrawsArrow)
            {
                _camTarget = ClampCamera(_camTarget - (pos - _lastPos) / Scale);
                _cam = _camTarget;
            }

            _lastPos = pos;
            return;
        }

        UpdateHover(pos);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hoveredSector = null;
        _hoveredPlanet = null;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (_diving != null)
            return;

        var cursor = args.RelativePixelPosition;
        var anchor = ToGalaxy(cursor);
        _zoomTarget = Math.Clamp(_zoomTarget * MathF.Pow(1.18f, args.Delta.Y), MinZoom, MaxZoom);
        // the point under the cursor stays where it is
        _camTarget = ClampCamera(anchor - (cursor - ViewCenter) / (BaseScale * _zoomTarget));
        args.Handle();
    }

    private static Vector2 ClampCamera(Vector2 cam)
    {
        return cam.Length() > 1.05f ? Vector2.Normalize(cam) * 1.05f : cam;
    }

    private void UpdateHover(Vector2 pixel)
    {
        // only the planet at war can be picked, the others are there to show the front
        int? planet = null;
        var best = 18f * MathF.Max(UiScale, 0.5f);
        for (var i = 0; i < _planets.Count; i++)
        {
            if (!_planets[i].Planet.War)
                continue;

            var distance = (ToScreen(_planets[i].Position) - pixel).Length();
            if (distance >= best)
                continue;

            best = distance;
            planet = i;
        }

        if (planet != _hoveredPlanet && planet != null)
            Sounds.Play(ST14HubSound.Hover);

        _hoveredPlanet = planet;
        _hoveredSector = planet == null ? SectorAt(ToGalaxy(pixel)) : null;
    }

    private ST14GalaxySectorPrototype? SectorAt(Vector2 galaxy)
    {
        var r = galaxy.Length();
        var angle = MathF.Atan2(galaxy.Y, galaxy.X) * 180f / MathF.PI;
        if (angle < 0)
            angle += 360;

        foreach (var (sector, _, _, _) in _sectors)
        {
            var (inner, outer) = Radii(sector);
            if (r < inner || r >= outer)
                continue;

            // a sector may run past 360 degrees
            if (sector.Ring == 0 || InRange(angle, sector) || InRange(angle + 360, sector))
                return sector;
        }

        return null;
    }

    private static bool InRange(float angle, ST14GalaxySectorPrototype sector)
    {
        return angle >= sector.Start && angle < sector.End;
    }

    private void ReleaseTool(Vector2 pixel)
    {
        if (Eraser)
        {
            if (MarkAt(pixel) is { } index)
            {
                Sounds.Play(ST14HubSound.Back);
                OnMarkErased?.Invoke(Marks[index].Side, Marks[index].Index);
            }

            return;
        }

        if (Tool is not { } kind)
            return;

        if (DrawsArrow)
        {
            if (!_dragging)
                return;

            Sounds.Play(ST14HubSound.Click);
            OnMarkPlaced?.Invoke(new ST14WarMark(kind, ToGalaxy(_pressPos), ToGalaxy(pixel)));
            return;
        }

        // a drag pans the map, only a click places the mark
        if (_dragging)
            return;

        Sounds.Play(ST14HubSound.Click);
        var point = ToGalaxy(pixel);
        OnMarkPlaced?.Invoke(new ST14WarMark(kind, point, point));
    }

    /// <summary>
    /// Mark under the cursor for the eraser: close to the sign, or to any point along an arrow. Only marks of the side the tools work for
    /// </summary>
    private int? MarkAt(Vector2 pixel)
    {
        int? best = null;
        var bestDistance = 18f * MathF.Max(UiScale, 0.5f);
        for (var i = 0; i < Marks.Count; i++)
        {
            if (Marks[i].Side != ToolSide)
                continue;

            var mark = Marks[i].Mark;
            var steps = mark.IsArrow ? 16 : 0;
            for (var s = 0; s <= steps; s++)
            {
                var point = mark.IsArrow ? ArrowPoint(mark, s / 16f) : ToScreen(mark.From);
                var distance = (point - pixel).Length();
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Arrows bend a little to the side so crossing ones stay readable
    /// </summary>
    private Vector2 ArrowPoint(ST14WarMark mark, float t)
    {
        var from = ToScreen(mark.From);
        var to = ToScreen(mark.To);
        var dir = to - from;
        var bend = new Vector2(-dir.Y, dir.X) * 0.15f;
        var control = (from + to) / 2 + bend;
        var u = 1 - t;
        return from * (u * u) + control * (2 * u * t) + to * (t * t);
    }

    #endregion

    #region Drawing

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var ui = MathF.Max(UiScale, 0.3f);
        var scale = Scale;
        var focused = SelectedSector != null;

        // the galaxy under the sectors
        _glow.Clear();
        foreach (var (pos, radius, color) in Clouds)
        {
            _glow.SpriteLinear(ToScreen(pos), radius * scale, color);
        }

        var starScale = ui * Math.Clamp(MathF.Sqrt(_zoom), 1, 2.4f);
        var bounds = new Box2(-20, -20, PixelSize.X + 20, PixelSize.Y + 20);
        foreach (var (pos, size, color, phase) in Stars)
        {
            var screen = ToScreen(pos);
            if (!bounds.Contains(screen))
                continue;

            var twinkle = 0.75f + 0.25f * MathF.Sin(_time * 1.7f + phase);
            _glow.SpriteLinear(screen, size * 2.2f * starScale, color.WithAlpha(color.A * twinkle));
        }

        _glow.Draw(handle, _textures.Glow);

        // enemy space: a tint and diagonal stripes, the stripes keep their size on screen
        _shapes.Clear();
        _hatch.Clear();
        foreach (var (sector, _, _, _) in _sectors)
        {
            var dim = focused && sector != SelectedSector;
            if (sector.Faction == ST14GalaxyFaction.Arachnid)
            {
                FillSector(sector, ST14GalaxyColors.Arachnid.WithAlpha(dim ? 0.08f : 0.13f), false);
                FillSector(sector, ST14GalaxyColors.Arachnid.WithAlpha(dim ? 0.18f : 0.32f), true);
            }

            if (sector == SelectedSector)
                FillSector(sector, ST14HubStyle.GoldLight.WithAlpha(0.06f), false);
            else if (sector == _hoveredSector)
                FillSector(sector, Color.White.WithAlpha(0.06f), false);
            else if (dim)
                FillSector(sector, Color.Black.WithAlpha(0.3f), false);
        }

        _hatch.Draw(handle, _textures.Hatch);

        // sector borders: thin lines, warmer inside enemy space
        var line = MathF.Max(1f, 1.1f * ui);
        foreach (var (sector, _, _, _) in _sectors)
        {
            var (inner, outer) = Radii(sector);
            var enemy = sector.Faction == ST14GalaxyFaction.Arachnid;
            var color = enemy ? ST14GalaxyColors.Arachnid.WithAlpha(0.5f) : LineColor.WithAlpha(focused ? 0.14f : 0.2f);
            Arc(outer, sector.Start, sector.End, line, color);
            if (sector.Ring > 0)
                _shapes.Line(ToScreen(Polar(inner, sector.Start)), ToScreen(Polar(outer, sector.Start)), line, color);
        }

        if (_hoveredSector != null && _hoveredSector != SelectedSector)
            Outline(_hoveredSector, line * 1.6f, Color.White.WithAlpha(0.5f));

        if (SelectedSector != null)
            Outline(SelectedSector, line * 2.2f, ST14HubStyle.GoldLight.WithAlpha(0.9f));

        // supply lines: a soft glow under a bright core, the lines into the war planet burn orange
        foreach (var (from, to) in _links)
        {
            var a = _planets[from];
            var b = _planets[to];
            var war = a.Planet.War || b.Planet.War;
            var color = war ? WarColor : SupplyColor;
            var pa = ToScreen(a.Position);
            var pb = ToScreen(b.Position);
            _shapes.Line(pa, pb, line * 4f, color.WithAlpha(0.12f));
            _shapes.Line(pa, pb, line * 1.4f, color.WithAlpha(war ? 0.85f : 0.55f));
        }

        _shapes.Draw(handle, Texture.White);

        DrawPlanets(handle, ui);
        DrawMarks(handle, ui);
        DrawLabels(handle, ui, focused);
    }

    private static Vector2 Polar(float radius, float degrees)
    {
        var a = Deg(degrees);
        return new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
    }

    private void FillSector(ST14GalaxySectorPrototype sector, Color color, bool hatch)
    {
        if (color.A <= 0)
            return;

        var period = MathF.Max(12f, 26f * MathF.Max(UiScale, 0.3f));
        var (inner, outer) = Radii(sector);
        for (var a = sector.Start; a < sector.End - 0.001f; a += ArcStep)
        {
            var b = MathF.Min(a + ArcStep, sector.End);
            var o0 = ToScreen(Polar(outer, a));
            var o1 = ToScreen(Polar(outer, b));
            var i0 = inner <= 0 ? ToScreen(Vector2.Zero) : ToScreen(Polar(inner, a));
            var i1 = inner <= 0 ? i0 : ToScreen(Polar(inner, b));

            if (hatch)
            {
                _hatch.PatternTriangle(i0, o0, o1, period, color);
                if (inner > 0)
                    _hatch.PatternTriangle(i0, o1, i1, period, color);
                continue;
            }

            _shapes.Triangle(i0, o0, o1, color);
            if (inner > 0)
                _shapes.Triangle(i0, o1, i1, color);
        }
    }

    private void Arc(float radius, float start, float end, float width, Color color)
    {
        for (var a = start; a < end - 0.001f; a += ArcStep)
        {
            var b = MathF.Min(a + ArcStep, end);
            _shapes.Line(ToScreen(Polar(radius, a)), ToScreen(Polar(radius, b)), width, color);
        }
    }

    private void Outline(ST14GalaxySectorPrototype sector, float width, Color color)
    {
        var (inner, outer) = Radii(sector);
        Arc(outer, sector.Start, sector.End, width, color);
        if (sector.Ring == 0)
            return;

        Arc(inner, sector.Start, sector.End, width, color);
        _shapes.Line(ToScreen(Polar(inner, sector.Start)), ToScreen(Polar(outer, sector.Start)), width, color);
        _shapes.Line(ToScreen(Polar(inner, sector.End)), ToScreen(Polar(outer, sector.End)), width, color);
    }

    private float MarkerSize(float ui)
    {
        return 5f * ui * Math.Clamp(MathF.Pow(_zoom, 0.4f), 1, 2.2f);
    }

    private void DrawPlanets(DrawingHandleScreen handle, float ui)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 3f);
        var size = MarkerSize(ui);

        _glow.Clear();
        _shapes.Clear();
        for (var i = 0; i < _planets.Count; i++)
        {
            var (planet, position, _) = _planets[i];
            var center = ToScreen(position);
            var hovered = i == _hoveredPlanet;

            if (planet == _diving)
            {
                // the planet grows with the dive until it fills the middle of the view
                var radius = MathF.Max(size * 1.6f, 0.0045f * Scale);
                var surface = planet.Surface.Count > 0 ? planet.Surface[planet.Surface.Count / 2] : Color.Gray;
                _glow.Sprite(center, radius * 1.6f, planet.Atmosphere.WithAlpha(0.6f));
                _shapes.Circle(center, radius, surface, 48);
                _shapes.Ring(center, radius * 0.55f, radius, Color.Black.WithAlpha(0f), Color.Black.WithAlpha(0.55f), 48);
                continue;
            }

            if (planet.Capital)
            {
                // Earth, the seat of the Federation
                var radius = size * 1.8f;
                _glow.Sprite(center, radius * 2.8f, planet.Atmosphere.WithAlpha(0.45f));
                _shapes.Circle(center, radius, Color.FromHex("#2E6B9E"), 40);
                _shapes.Circle(center + new Vector2(-radius * 0.25f, -radius * 0.1f), radius * 0.45f, Color.FromHex("#3F7A3A"), 24);
                _shapes.Ring(center, radius * 0.6f, radius, Color.Black.WithAlpha(0f), Color.Black.WithAlpha(0.45f), 40);
                _shapes.Ring(center, radius * 1.18f, radius * 1.18f + MathF.Max(1, 1.5f * ui), Color.White.WithAlpha(0.5f), Color.White.WithAlpha(0.5f), 40);
                continue;
            }

            if (!planet.War)
            {
                // planets out of reach: small grey discs with the colour of their side
                var locked = Color.InterpolateBetween(ST14GalaxyColors.Of(planet.Faction), new Color(0.55f, 0.58f, 0.62f), 0.55f);
                _shapes.Circle(center, size * 0.95f, locked.WithAlpha(0.9f), 16);
                _shapes.Circle(center, size * 0.55f, Color.FromHex("#141A22"), 16);
                continue;
            }

            // the planet at war: lit, ringed and pulsing
            var war = size * 1.5f;
            _glow.Sprite(center, war * 3.4f, WarColor.WithAlpha(hovered ? 0.6f : 0.4f));
            var wave = (_time * 0.6f) % 1f;
            var waveRadius = war * (1.5f + wave * 2.4f);
            _shapes.Ring(center, waveRadius, waveRadius + MathF.Max(1, 1.5f * ui), WarColor.WithAlpha(0.8f * (1 - wave)),
                WarColor.WithAlpha(0.8f * (1 - wave)));
            _shapes.Circle(center, war, planet.Surface.Count > 0 ? planet.Surface[planet.Surface.Count - 2] : Color.Gray, 24);
            _shapes.Ring(center, war * 0.5f, war, Color.Black.WithAlpha(0f), Color.Black.WithAlpha(0.5f), 24);
            _shapes.Ring(center, war * 1.25f, war * 1.25f + MathF.Max(1.5f, 2.2f * ui), WarColor.WithAlpha(0.75f + 0.25f * pulse),
                WarColor.WithAlpha(0.75f + 0.25f * pulse), 32);
            if (hovered)
                _shapes.Ring(center, war * 1.6f, war * 1.6f + MathF.Max(1, 1.5f * ui), Color.White, Color.White, 32);

            DrawTarget(center + new Vector2(0, -war * 3.1f), war * 1.15f, ui);
        }

        _glow.Draw(handle, _textures.Glow);
        _shapes.Draw(handle, Texture.White);
    }

    /// <summary>
    /// The bugs plan in their own colours, so a commander who sees both plans tells them apart
    /// </summary>
    private static Color MarkColor(ST14GalaxyFaction side, ST14WarMarkKind kind)
    {
        if (side == ST14GalaxyFaction.Arachnid)
        {
            return kind switch
            {
                ST14WarMarkKind.Attack => Color.FromHex("#FF9A2E"),
                ST14WarMarkKind.Defend => Color.FromHex("#B05CE0"),
                ST14WarMarkKind.Rally => Color.FromHex("#C8E040"),
                ST14WarMarkKind.Danger => Color.FromHex("#FF5A5A"),
                _ => Color.FromHex("#FFC890"),
            };
        }

        return kind switch
        {
            ST14WarMarkKind.Attack => Color.FromHex("#FF4A2E"),
            ST14WarMarkKind.Defend => ST14GalaxyColors.Federation,
            ST14WarMarkKind.Rally => Color.FromHex("#5CD67A"),
            ST14WarMarkKind.Danger => Color.FromHex("#FFD23A"),
            _ => Color.White,
        };
    }

    /// <summary>
    /// The plan of the High Command: bent arrows with heads and signs for rally points, danger and objectives
    /// </summary>
    private void DrawMarks(DrawingHandleScreen handle, float ui)
    {
        if (_diving != null)
            return;

        _glow.Clear();
        _shapes.Clear();
        var width = MathF.Max(2.5f, 5f * ui);
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 4f);

        foreach (var (side, _, mark) in Marks)
        {
            AddMark(side, mark, width, pulse, 1f);
        }

        // the arrow being drawn follows the cursor
        if (_pressed && _dragging && DrawsArrow && Tool is { } kind)
            AddMark(ToolSide, new ST14WarMark(kind, ToGalaxy(_pressPos), ToGalaxy(_lastPos)), width, pulse, 0.55f);

        _glow.Draw(handle, _textures.Glow);
        _shapes.Draw(handle, Texture.White);
    }

    private void AddMark(ST14GalaxyFaction side, ST14WarMark mark, float width, float pulse, float alpha)
    {
        var color = MarkColor(side, mark.Kind);
        if (mark.IsArrow)
        {
            const int segments = 20;
            var points = new Vector2[segments + 1];
            for (var i = 0; i <= segments; i++)
            {
                points[i] = ArrowPoint(mark, i / (float) segments);
            }

            // the body stops short of the tip, the head covers the rest
            var head = width * 4.2f;
            var tip = points[segments];
            var back = tip - Vector2.Normalize(tip - points[segments - 2]) * head;
            for (var i = 0; i < segments; i++)
            {
                var a = points[i];
                var b = i == segments - 1 ? back : points[i + 1];
                _shapes.Line(a, b, width * 2.6f, color.WithAlpha(0.18f * alpha));
                _shapes.Line(a, b, width, color.WithAlpha(0.9f * alpha));
            }

            // light runs along an attack arrow towards its head
            if (mark.Kind == ST14WarMarkKind.Attack)
            {
                var run = (_time * 0.8f) % 1f;
                var p = ArrowPoint(mark, run * 0.9f);
                _glow.Sprite(p, width * 2.5f, Color.White.WithAlpha(0.7f * alpha));
            }

            var dir = Vector2.Normalize(tip - back);
            var wing = new Vector2(-dir.Y, dir.X) * head * 0.6f;
            _shapes.Triangle(tip, back + wing, back - wing, color.WithAlpha(alpha));
            _glow.Sprite(tip, head * 1.4f, color.WithAlpha(0.35f * alpha));
            return;
        }

        var center = ToScreen(mark.From);
        var size = width * 3.2f;
        _glow.Sprite(center, size * 2.2f, color.WithAlpha((0.25f + 0.15f * pulse) * alpha));
        switch (mark.Kind)
        {
            case ST14WarMarkKind.Rally:
                // a flag on a pole
                _shapes.Line(center + new Vector2(-size * 0.4f, size), center + new Vector2(-size * 0.4f, -size), width * 0.6f,
                    color.WithAlpha(alpha));
                _shapes.Triangle(center + new Vector2(-size * 0.4f, -size), center + new Vector2(size * 0.9f, -size * 0.55f),
                    center + new Vector2(-size * 0.4f, -size * 0.1f), color.WithAlpha(alpha));
                break;
            case ST14WarMarkKind.Danger:
                // a warning triangle with an exclamation mark
                _shapes.Triangle(center + new Vector2(0, -size), center + new Vector2(size, size * 0.8f), center + new Vector2(-size, size * 0.8f),
                    color.WithAlpha(alpha));
                var dark = Color.FromHex("#141A22").WithAlpha(alpha);
                _shapes.Line(center + new Vector2(0, -size * 0.45f), center + new Vector2(0, size * 0.2f), width * 0.55f, dark);
                _shapes.Circle(center + new Vector2(0, size * 0.48f), width * 0.32f, dark, 8);
                break;
            default:
                DrawTarget(center, size, 1f);
                break;
        }
    }

    /// <summary>
    /// White hexagon with a crosshair above the planet of the current operation
    /// </summary>
    private void DrawTarget(Vector2 center, float radius, float ui)
    {
        var corners = new Vector2[6];
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.PI / 3 * i + MathF.PI / 6;
            corners[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }

        for (var i = 0; i < 6; i++)
        {
            _shapes.Triangle(center, corners[i], corners[(i + 1) % 6], Color.White.WithAlpha(0.95f));
        }

        var dark = Color.FromHex("#141A22");
        var width = MathF.Max(1f, radius * 0.12f);
        _shapes.Ring(center, radius * 0.38f, radius * 0.38f + width, dark, dark, 20);
        _shapes.Circle(center, radius * 0.12f, dark, 10);
        _shapes.Line(center + new Vector2(0, -radius * 0.7f), center + new Vector2(0, -radius * 0.42f), width, dark);
        _shapes.Line(center + new Vector2(0, radius * 0.7f), center + new Vector2(0, radius * 0.42f), width, dark);
        _shapes.Line(center + new Vector2(-radius * 0.7f, 0), center + new Vector2(-radius * 0.42f, 0), width, dark);
        _shapes.Line(center + new Vector2(radius * 0.7f, 0), center + new Vector2(radius * 0.42f, 0), width, dark);
    }

    private void DrawLabels(DrawingHandleScreen handle, float ui, bool focused)
    {
        if (_diving != null)
            return;

        // the name of the enemy along the rim, letter by letter
        var zoneSize = (int) Math.Clamp(MathF.Round(0.055f * Scale), 10, 140);
        var zoneFont = ST14HubStyle.Font(_cache, ST14HubFont.Black, zoneSize);
        var zoneRadius = (EdgeRadius - 0.045f) * Scale;
        foreach (var (angle, text) in _zoneLabels)
        {
            var widths = text.Select(c => handle.GetDimensions(zoneFont, c.ToString(), 1f).X + zoneSize * 0.25f).ToArray();
            var total = widths.Sum() / zoneRadius * 180f / MathF.PI;
            var at = angle - total / 2;
            for (var i = 0; i < text.Length; i++)
            {
                var step = widths[i] / zoneRadius * 180f / MathF.PI;
                var mid = at + step / 2;
                at += step;
                ST14HubStyle.DrawRotatedText(handle, zoneFont, text[i].ToString(), ToScreen(Polar(EdgeRadius - 0.045f, mid)), mid + 90,
                    ST14GalaxyColors.Arachnid.WithAlpha(0.75f));
            }
        }

        // sector names appear once the table is close enough to read them
        var sectorSize = (int) Math.Clamp(MathF.Round(10 * ui * MathF.Sqrt(_zoom)), 8, 26);
        var sectorFont = ST14HubStyle.Font(_cache, ST14HubFont.Medium, sectorSize);
        var fade = Math.Clamp((_zoom - 1.4f) / 0.6f, 0, 1);
        foreach (var (sector, label, _, name) in _sectors)
        {
            var selected = sector == SelectedSector;
            var hovered = sector == _hoveredSector;
            var alpha = selected ? 0.95f : hovered ? 0.85f : fade * (focused ? 0.3f : 0.55f);
            if (alpha <= 0.01f)
                continue;

            var color = selected ? ST14HubStyle.GoldLight
                : sector.Faction == ST14GalaxyFaction.Arachnid ? Color.InterpolateBetween(ST14GalaxyColors.Arachnid, Color.White, 0.45f)
                : new Color(0.76f, 0.83f, 0.92f);
            // the core label sits under the bright centre
            var pos = ToScreen(label) + (sector.Ring == 0 ? new Vector2(0, sectorSize * 2.4f) : Vector2.Zero);
            ST14HubStyle.DrawRotatedText(handle, sectorFont, name, pos, 0, color.WithAlpha(alpha), ST14HubStyle.TextShadow);
        }

        var planetSize = (int) Math.Clamp(MathF.Round(13 * ui * MathF.Pow(_zoom, 0.3f)), 9, 24);
        var planetFont = ST14HubStyle.Font(_cache, ST14HubFont.Regular, planetSize);
        var size = MarkerSize(ui);
        for (var i = 0; i < _planets.Count; i++)
        {
            var (planet, position, name) = _planets[i];
            var hovered = i == _hoveredPlanet;
            var inSector = SelectedSector != null && planet.Sector == SelectedSector.ID;
            if (!planet.War && !planet.Capital && _zoom < 2.2f && !inSector)
                continue;

            var color = planet.War ? (hovered ? Color.White : WarColor)
                : planet.Capital ? new Color(0.85f, 0.9f, 0.95f)
                : new Color(0.7f, 0.74f, 0.8f, 0.8f);
            var offset = (planet.Capital ? size * 2.6f : planet.War ? size * 2.6f : size * 1.6f) + planetSize * 0.6f;
            ST14HubStyle.DrawRotatedText(handle, planetFont, name, ToScreen(position) + new Vector2(0, offset), 0, color,
                ST14HubStyle.TextShadow);
        }
    }

    #endregion
}
