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
/// The Milky Way seen from above, split into sectors with the planets and supply lines on top.
/// The camera pans with the mouse, zooms with the wheel and flies to the sector or planet the screen asks for
/// </summary>
public sealed class ST14GalaxyMapView : Control
{
    private const float DragThreshold = 5f;
    private const float ArcStep = 2f;
    private const float MinZoom = 0.85f;
    private const float MaxZoom = 7f;

    private readonly IResourceCache _cache;
    private readonly ST14GalaxyTextures _textures;
    private readonly ST14VertexBuffer _glow = new();
    private readonly ST14VertexBuffer _shapes = new();
    private readonly List<(ST14GalaxySectorPrototype Sector, Vector2 Label, float Extent, string Name)> _sectors = new();
    private readonly List<(ST14GalaxyPlanetPrototype Planet, Vector2 Position, string Name)> _planets = new();
    private readonly List<(int From, int To)> _links = new();

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

    public ST14GalaxySectorPrototype? SelectedSector;
    public ST14GalaxyPlanetPrototype? SelectedPlanet;

    public event Action<ST14GalaxySectorPrototype>? OnSectorClicked;
    public event Action<ST14GalaxyPlanetPrototype>? OnPlanetClicked;
    public event Action? OnEmptyClicked;
    public event Action? OnBackClicked;

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

        var stars = new List<(Vector2, float, Color, float)>();
        var warm = new Color(1f, 0.86f, 0.62f);
        var blue = new Color(0.76f, 0.85f, 1f);
        var pink = new Color(1f, 0.6f, 0.78f);

        // bulge and bar
        for (var i = 0; i < 1400; i++)
        {
            var p = new Vector2(Gauss() * 0.13f, Gauss() * 0.055f);
            p = Vector2.Transform(p, Matrix3x2.CreateRotation(bar));
            stars.Add((p, 0.6f + random.NextSingle() * 1.1f, Color.InterpolateBetween(warm, Color.White, random.NextSingle() * 0.4f),
                random.NextSingle() * MathF.Tau));
        }

        // spiral arms
        for (var i = 0; i < 5600; i++)
        {
            var r = 0.12f + 0.86f * MathF.Sqrt(random.NextSingle());
            var p = ArmPoint(PickArm(), r, 0.022f + 0.04f * r);
            var roll = random.NextSingle();
            var color = roll < 0.04f ? pink : roll < 0.25f ? Color.InterpolateBetween(warm, Color.White, 0.5f) : blue;
            stars.Add((p, 0.5f + random.NextSingle() * random.NextSingle() * 1.6f, color, random.NextSingle() * MathF.Tau));
        }

        // thin disc between the arms
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
            clouds.Add((ArmPoint(PickArm(), r, 0.02f), 0.05f + random.NextSingle() * 0.07f,
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
        _camTarget = Vector2.Zero;
        _zoomTarget = 1;
    }

    public void FocusSector(ST14GalaxySectorPrototype sector)
    {
        var entry = _sectors.FirstOrDefault(s => s.Sector == sector);
        if (entry.Sector == null)
            return;

        _camTarget = entry.Label;
        _zoomTarget = Math.Clamp(0.62f / entry.Extent, 1.5f, 5.5f);
    }

    public void FocusPlanet(ST14GalaxyPlanetPrototype planet)
    {
        var entry = _planets.FirstOrDefault(p => p.Planet == planet);
        if (entry.Planet == null)
            return;

        _camTarget = entry.Position;
        _zoomTarget = MathF.Max(_zoomTarget, 3.4f);
    }

    public Vector2 PositionOf(ST14GalaxyPlanetPrototype planet)
    {
        return _planets.FirstOrDefault(p => p.Planet == planet).Position;
    }

    private float UiScale => MathF.Min(PixelSize.X / ST14CanvasLayout.CanvasSize.X, PixelSize.Y / ST14CanvasLayout.CanvasSize.Y);

    private float BaseScale => MathF.Min(PixelSize.X, PixelSize.Y) * 0.44f;

    private float Scale => BaseScale * _zoom;

    private Vector2 ViewCenter => new(PixelSize.X * (1 - _inset) / 2, PixelSize.Y * 0.47f);

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
        var k = 1 - MathF.Exp(-dt * 5f);
        _cam = Vector2.Lerp(_cam, _camTarget, k);
        _zoom += (_zoomTarget - _zoom) * k;
        _inset += (_insetTarget - _inset) * k;
    }

    #region Input

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
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

        var pos = args.RelativePixelPosition;
        if (_pressed)
        {
            if (!_dragging && (pos - _pressPos).Length() > DragThreshold)
                _dragging = true;

            if (_dragging)
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
        int? planet = null;
        var best = 16f * MathF.Max(UiScale, 0.5f);
        for (var i = 0; i < _planets.Count; i++)
        {
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

            if (sector.Ring == 0 || angle >= sector.Start && angle < sector.End)
                return sector;
        }

        return null;
    }

    #endregion

    #region Drawing

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var ui = MathF.Max(UiScale, 0.3f);
        var scale = Scale;
        var focused = SelectedSector != null;

        // nebulae and stars
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

        // sector fills and borders
        _shapes.Clear();
        foreach (var (sector, _, _, _) in _sectors)
        {
            var fill = ST14GalaxyColors.Of(sector.Faction).WithAlpha(sector.Faction switch
            {
                ST14GalaxyFaction.Federation => 0.07f,
                ST14GalaxyFaction.Arachnid => 0.1f,
                ST14GalaxyFaction.Contested => 0.06f,
                _ => 0f,
            });

            if (sector == SelectedSector)
                fill = ST14HubStyle.GoldLight.WithAlpha(0.07f);
            else if (focused)
                fill = Color.Black.WithAlpha(0.38f);

            if (sector == _hoveredSector && sector != SelectedSector)
                fill = Color.InterpolateBetween(fill, Color.White, 0.5f).WithAlpha(fill.A + 0.07f);

            FillSector(sector, fill);
        }

        var line = MathF.Max(1f, 1.2f * ui);
        foreach (var (sector, _, _, _) in _sectors)
        {
            var (inner, outer) = Radii(sector);
            var color = ST14GalaxyColors.Line.WithAlpha(focused ? 0.16f : 0.24f);
            Arc(outer, sector.Start, sector.End, line, color);
            if (sector.Ring > 0)
                _shapes.Line(ToScreen(Polar(inner, sector.Start)), ToScreen(Polar(outer, sector.Start)), line, color);
        }

        if (_hoveredSector != null && _hoveredSector != SelectedSector)
            Outline(_hoveredSector, line * 1.6f, Color.White.WithAlpha(0.55f));

        if (SelectedSector != null)
            Outline(SelectedSector, line * 2.2f, ST14HubStyle.GoldLight.WithAlpha(0.9f));

        // supply lines
        foreach (var (from, to) in _links)
        {
            var a = _planets[from];
            var b = _planets[to];
            var lit = a.Planet == SelectedPlanet || b.Planet == SelectedPlanet;
            _shapes.Line(ToScreen(a.Position), ToScreen(b.Position), line * (lit ? 1.6f : 1.1f),
                ST14GalaxyColors.Line.WithAlpha(lit ? 0.75f : 0.32f));
        }

        _shapes.Draw(handle, Texture.White);

        DrawPlanets(handle, ui);
        DrawLabels(handle, ui, focused);
    }

    private static Vector2 Polar(float radius, float degrees)
    {
        var a = Deg(degrees);
        return new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
    }

    private void FillSector(ST14GalaxySectorPrototype sector, Color color)
    {
        if (color.A <= 0)
            return;

        var (inner, outer) = Radii(sector);
        for (var a = sector.Start; a < sector.End - 0.001f; a += ArcStep)
        {
            var b = MathF.Min(a + ArcStep, sector.End);
            var o0 = ToScreen(Polar(outer, a));
            var o1 = ToScreen(Polar(outer, b));
            if (inner <= 0)
            {
                _shapes.Triangle(ToScreen(Vector2.Zero), o0, o1, color);
                continue;
            }

            var i0 = ToScreen(Polar(inner, a));
            var i1 = ToScreen(Polar(inner, b));
            _shapes.Triangle(i0, o0, o1, color);
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

    private void DrawPlanets(DrawingHandleScreen handle, float ui)
    {
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 3f);
        var size = 5f * ui * Math.Clamp(MathF.Pow(_zoom, 0.4f), 1, 2.2f);

        _glow.Clear();
        _shapes.Clear();
        for (var i = 0; i < _planets.Count; i++)
        {
            var (planet, position, _) = _planets[i];
            var center = ToScreen(position);
            var color = ST14GalaxyColors.Of(planet.Faction);
            var radius = planet.Capital ? size * 1.45f : size;
            var hovered = i == _hoveredPlanet;
            var selected = planet == SelectedPlanet;

            _glow.Sprite(center, radius * 3.2f, color.WithAlpha(hovered || selected ? 0.55f : 0.32f));

            if (planet.War)
            {
                // the war planet sends out rings like a beacon
                var wave = (_time * 0.6f) % 1f;
                _shapes.Ring(center, radius * (1.4f + wave * 2.2f), radius * (1.4f + wave * 2.2f) + MathF.Max(1, 1.5f * ui),
                    ST14GalaxyColors.Contested.WithAlpha(0.8f * (1 - wave)), ST14GalaxyColors.Contested.WithAlpha(0.8f * (1 - wave)));
            }

            _shapes.Circle(center, radius, color);
            _shapes.Circle(center, radius * 0.62f, Color.FromHex("#0B1118"));
            _shapes.Circle(center, radius * (planet.Capital ? 0.4f : 0.3f), planet.Capital ? Color.White : color);

            if (selected)
                _shapes.Ring(center, radius * 1.7f, radius * 1.7f + MathF.Max(1.5f, 2f * ui),
                    ST14HubStyle.GoldLight.WithAlpha(0.7f + 0.3f * pulse), ST14HubStyle.GoldLight.WithAlpha(0.7f + 0.3f * pulse));
            else if (hovered)
                _shapes.Ring(center, radius * 1.5f, radius * 1.5f + MathF.Max(1, 1.5f * ui), Color.White, Color.White);
        }

        _glow.Draw(handle, _textures.Glow);
        _shapes.Draw(handle, Texture.White);
    }

    private void DrawLabels(DrawingHandleScreen handle, float ui, bool focused)
    {
        var sectorSize = (int) Math.Clamp(MathF.Round(12 * ui * MathF.Sqrt(_zoom)), 9, 30);
        var sectorFont = ST14HubStyle.Font(_cache, ST14HubFont.Medium, sectorSize);
        foreach (var (sector, label, _, name) in _sectors)
        {
            var selected = sector == SelectedSector;
            var alpha = selected ? 0.95f : focused ? 0.28f : sector == _hoveredSector ? 0.9f : 0.55f;
            var pos = ToScreen(label);
            // the core label sits under the bright centre
            if (sector.Ring == 0)
                pos += new Vector2(0, sectorSize * 2.2f);

            var color = selected ? ST14HubStyle.GoldLight : new Color(0.76f, 0.83f, 0.92f);
            ST14HubStyle.DrawRotatedText(handle, sectorFont, name, pos, 0, color.WithAlpha(alpha), ST14HubStyle.TextShadow);
        }

        var planetSize = (int) Math.Clamp(MathF.Round(13 * ui * MathF.Pow(_zoom, 0.3f)), 9, 24);
        var planetFont = ST14HubStyle.Font(_cache, ST14HubFont.Regular, planetSize);
        var offset = 5f * ui * Math.Clamp(MathF.Pow(_zoom, 0.4f), 1, 2.2f) * 2.6f + planetSize * 0.6f;
        for (var i = 0; i < _planets.Count; i++)
        {
            var (planet, position, name) = _planets[i];
            var selected = planet == SelectedPlanet;
            var show = _zoom >= 2.2f || selected || i == _hoveredPlanet || SelectedSector != null && planet.Sector == SelectedSector.ID || planet.Capital || planet.War;
            if (!show)
                continue;

            var color = selected ? ST14HubStyle.GoldLight : i == _hoveredPlanet ? Color.White : new Color(0.88f, 0.9f, 0.95f);
            ST14HubStyle.DrawRotatedText(handle, planetFont, name, ToScreen(position) + new Vector2(0, offset), 0, color,
                ST14HubStyle.TextShadow);
        }
    }

    #endregion
}
