using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub.Galaxy;

public enum ST14GalaxyLevel : byte
{
    Galaxy,
    Sector,
    Dive,
    Surface,
}

/// <summary>
/// Galactic war map in the way of the war table of Helldivers 2: the Milky Way split into rings of sectors around Earth,
/// a sector opens with its planets, a click on the war planet dives into it, and there the planet turns under the mouse
/// with its surface split into hex sectors where troopers land
/// </summary>
public sealed partial class ST14GalaxyMapScreen : ST14WarScreenBase
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private const float PanelInset = 0.28f;
    private const float DiveTime = 0.9f;
    private const float GlobeZoom = 0.8f;
    private static readonly Vector2 PanelSize = new(520, 800);
    private const float PanelPad = 30;

    private readonly ST14GalaxyTextures _textures = new();
    private readonly Dictionary<string, ST14PlanetWar> _wars = new();
    private readonly ST14GalaxyMapView _map;
    private readonly ST14PlanetGlobe _surface;
    private readonly PanelContainer _panelBack;
    private readonly ST14CanvasLayout _panel;
    private readonly Label _path;
    private readonly Label _stats;
    private readonly Label _hint;

    private ST14GalaxyLevel _level;
    private ST14GalaxySectorPrototype? _sector;
    private ST14GalaxyPlanetPrototype? _planet;
    private float _dive;

    /// <summary>
    /// A landing was ordered: into the lobby
    /// </summary>
    public event Action? OnLanding;

    public ST14GalaxyMapScreen() : base("ST14HubGalaxyMap")
    {
        IoCManager.InjectDependencies(this);

        _map = new ST14GalaxyMapView(_prototypes, ResCache, _textures);
        _map.OnSectorClicked += SectorClicked;
        _map.OnPlanetClicked += Dive;
        _map.OnEmptyClicked += () =>
        {
            if (_level != ST14GalaxyLevel.Galaxy)
                GoBack();
        };
        _map.OnBackClicked += GoBack;
        Canvas.AddFull(_map);

        _surface = new ST14PlanetGlobe(_textures)
        {
            Visible = false,
            Zoom = GlobeZoom,
            MinZoom = 0.6f,
            MaxZoom = 1.05f,
        };
        _surface.OnCellSelected += cell => BuildSurfacePanel(cell);
        Canvas.AddAt(_surface, new Vector2(0.5f, 0.5f), new Vector2(-270, -25), new Vector2(900, 900));

        // header: title, where the map is, and the state of the war on the right
        var title = ST14HubStyle.Label(ResCache, Loc.GetString("st14-galaxy-title").ToUpperInvariant(), ST14HubFont.Black, 38,
            ST14HubStyle.GoldLight, shadow: true);
        Canvas.AddAt(title, Vector2.Zero, new Vector2(60, 36), new Vector2(1000, 56), Vector2.Zero);

        _path = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Regular, 19, ST14HubStyle.Gold, shadow: true);
        Canvas.AddAt(_path, Vector2.Zero, new Vector2(62, 96), new Vector2(1200, 32), Vector2.Zero);

        _stats = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Medium, 18, ST14HubStyle.GoldLight, shadow: true);
        _stats.Align = Label.AlignMode.Right;
        Canvas.AddAt(_stats, new Vector2(1, 0), new Vector2(-60, 44), new Vector2(800, 30), new Vector2(1, 0));

        var legend = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Align = BoxContainer.AlignMode.End,
            SeparationOverride = 18,
        };
        foreach (var faction in new[] { ST14GalaxyFaction.Federation, ST14GalaxyFaction.Arachnid })
        {
            legend.AddChild(new PanelContainer
            {
                PanelOverride = ST14HubStyle.Flat(ST14GalaxyColors.Of(faction)),
                MinSize = new Vector2(12, 12),
                VerticalAlignment = VAlignment.Center,
            });
            var name = ST14HubStyle.Label(ResCache, FactionName(faction).ToUpperInvariant(), ST14HubFont.Regular, 15,
                new Color(0.85f, 0.88f, 0.92f), shadow: true);
            name.VAlign = Label.VAlignMode.Center;
            legend.AddChild(name);
        }
        Canvas.AddAt(legend, new Vector2(1, 0), new Vector2(-60, 82), new Vector2(800, 28), new Vector2(1, 0));

        // side panel, its content is rebuilt for every level
        _panelBack = new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(new Color(0.02f, 0.035f, 0.06f, 0.86f), ST14HubStyle.Gold.WithAlpha(0.45f), 1.5f),
            // clicks on the panel must not reach the map below and count as a click into empty space
            MouseFilter = MouseFilterMode.Stop,
            Visible = false,
        };
        Canvas.AddAt(_panelBack, new Vector2(1, 0.5f), new Vector2(-40, -25), PanelSize, new Vector2(1, 0.5f));
        _panel = new ST14CanvasLayout { Visible = false };
        Canvas.AddAt(_panel, new Vector2(1, 0.5f), new Vector2(-40, -25), PanelSize, new Vector2(1, 0.5f));

        AddBottomBar();

        _hint = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Regular, 19, ST14HubStyle.Gold, shadow: true);
        _hint.Align = Label.AlignMode.Center;
        _hint.VAlign = Label.VAlignMode.Center;
        Canvas.AddAt(_hint, new Vector2(0.5f, 1), new Vector2(0, -46.5f), new Vector2(1300, 50));

        UpdateStats();
        ShowGalaxy();
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);

        // the map opens on the whole galaxy every time
        if (newVisible)
            ShowGalaxy();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_level != ST14GalaxyLevel.Dive || _planet == null)
            return;

        _dive += args.DeltaSeconds;
        if (_dive >= DiveTime)
            ShowSurface(_planet);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIRightClick || _level is ST14GalaxyLevel.Galaxy or ST14GalaxyLevel.Dive)
            return;

        GoBack();
        args.Handle();
    }

    protected override void GoBack()
    {
        switch (_level)
        {
            case ST14GalaxyLevel.Dive:
                break;
            case ST14GalaxyLevel.Surface when _sector != null:
                ShowSector(_sector);
                break;
            case ST14GalaxyLevel.Surface:
            case ST14GalaxyLevel.Sector:
                ShowGalaxy();
                break;
            default:
                base.GoBack();
                break;
        }
    }

    private void SectorClicked(ST14GalaxySectorPrototype sector)
    {
        if (_level == ST14GalaxyLevel.Sector && sector == _sector)
            return;

        ShowSector(sector);
    }

    #region Levels

    private void ShowGalaxy()
    {
        _level = ST14GalaxyLevel.Galaxy;
        _sector = null;
        _planet = null;
        _map.SelectedSector = null;
        _map.FocusGalaxy();
        _map.PanelInset = 0;
        ShowMap(true);
        ShowPanel(false);
        SetPath();
        _hint.Text = Loc.GetString("st14-galaxy-hint-galaxy").ToUpperInvariant();
    }

    private void ShowSector(ST14GalaxySectorPrototype sector)
    {
        _level = ST14GalaxyLevel.Sector;
        _sector = sector;
        _planet = null;
        _map.SelectedSector = sector;
        _map.FocusSector(sector);
        _map.PanelInset = PanelInset;
        ShowMap(true);
        SetPath(sector);
        _hint.Text = Loc.GetString("st14-galaxy-hint-sector").ToUpperInvariant();
        BuildSectorPanel(sector);
    }

    /// <summary>
    /// The camera falls onto the war planet, the planet itself takes over when the dive ends
    /// </summary>
    private void Dive(ST14GalaxyPlanetPrototype planet)
    {
        if (!planet.War || _level == ST14GalaxyLevel.Dive)
            return;

        _level = ST14GalaxyLevel.Dive;
        _planet = planet;
        _sector = _prototypes.TryIndex(planet.Sector, out var sector) ? sector : null;
        _dive = 0;
        _map.SelectedSector = null;
        _map.PanelInset = PanelInset;
        _map.Dive(planet);
        ShowMap(true);
        ShowPanel(false);
        SetPath(_sector, planet);
        _hint.Text = string.Empty;
    }

    private void ShowSurface(ST14GalaxyPlanetPrototype planet)
    {
        _level = ST14GalaxyLevel.Surface;
        _planet = planet;
        ShowMap(false);
        _surface.SetPlanet(planet, WarOf(planet));
        _surface.Intro(0.12f, GlobeZoom);
        SetPath(_sector, planet, true);
        _hint.Text = Loc.GetString("st14-galaxy-hint-surface").ToUpperInvariant();
        BuildSurfacePanel(null);
    }

    private void ShowMap(bool map)
    {
        _map.Visible = map;
        _surface.Visible = !map;
    }

    private void ShowPanel(bool visible)
    {
        _panelBack.Visible = visible;
        _panel.Visible = visible;
    }

    private void SetPath(ST14GalaxySectorPrototype? sector = null, ST14GalaxyPlanetPrototype? planet = null, bool surface = false)
    {
        var parts = new List<string> { Loc.GetString("st14-galaxy-milky-way") };
        if (sector != null)
            parts.Add(Loc.GetString(sector.Name));
        if (planet != null)
            parts.Add(Loc.GetString(planet.Name));
        if (surface)
            parts.Add(Loc.GetString("st14-galaxy-path-surface"));

        _path.Text = string.Join("  /  ", parts).ToUpperInvariant();
    }

    private void UpdateStats()
    {
        var sectors = _map.Sectors.ToList();
        var held = sectors.Count(s => s.Faction == ST14GalaxyFaction.Federation);
        _stats.Text = Loc.GetString("st14-galaxy-stats", ("held", held), ("total", sectors.Count)).ToUpperInvariant();
    }

    private ST14PlanetWar? WarOf(ST14GalaxyPlanetPrototype planet)
    {
        if (!planet.War)
            return null;

        if (_wars.TryGetValue(planet.ID, out var war))
            return war;

        var name = Loc.GetString(planet.Name);
        war = new ST14PlanetWar(planet, name.Length > 0 ? name[..1].ToUpperInvariant() : "X");
        _wars[planet.ID] = war;
        return war;
    }

    private static string FactionName(ST14GalaxyFaction faction)
    {
        return Loc.GetString($"st14-galaxy-faction-{faction.ToString().ToLowerInvariant()}");
    }

    #endregion

    #region Panel

    private void ClearPanel()
    {
        _panel.RemoveAllChildren();
        ShowPanel(true);
    }

    private void FinishPanel()
    {
        // new labels take the font size of the current window right away
        ST14HubStyle.ScaleLabels(_panel, Canvas.CanvasScale);
    }

    private Label AddText(string text, ST14HubFont font, int size, Color color, float y, float height, bool center = false)
    {
        var label = ST14HubStyle.Label(ResCache, text, font, size, color, shadow: true);
        label.VAlign = Label.VAlignMode.Top;
        if (center)
            label.Align = Label.AlignMode.Center;

        _panel.AddAt(label, Vector2.Zero, new Vector2(PanelPad, y), new Vector2(PanelSize.X - PanelPad * 2, height), Vector2.Zero);
        return label;
    }

    private void AddDivider(float y)
    {
        _panel.AddAt(new PanelContainer { PanelOverride = ST14HubStyle.Flat(ST14HubStyle.Gold.WithAlpha(0.4f)) },
            Vector2.Zero, new Vector2(PanelPad, y), new Vector2(PanelSize.X - PanelPad * 2, 1.5f), Vector2.Zero);
    }

    private ST14HubButton AddButton(string text, float y, Color? tint = null, bool disabled = false)
    {
        var button = new ST14HubButton(ResCache, text, 19, tint, disabled ? null : Color.White)
        {
            Disabled = disabled,
        };
        _panel.AddAt(button, Vector2.Zero, new Vector2(PanelSize.X / 2, y), new Vector2(PanelSize.X - PanelPad * 2 - 40, 62),
            new Vector2(0.5f, 0));
        return button;
    }

    private void AddBar(float liberation, float y)
    {
        var bar = new ST14GalaxyBar(ResCache)
        {
            Fraction = liberation / 100f,
            Text = Loc.GetString("st14-galaxy-liberation", ("percent", MathF.Round(liberation))).ToUpperInvariant(),
        };
        _panel.AddAt(bar, Vector2.Zero, new Vector2(PanelPad, y), new Vector2(PanelSize.X - PanelPad * 2, 32), Vector2.Zero);
    }

    private void AddFaction(ST14GalaxyFaction faction, float y)
    {
        AddText(Loc.GetString("st14-galaxy-control", ("faction", FactionName(faction))).ToUpperInvariant(), ST14HubFont.Medium, 17,
            ST14GalaxyColors.Of(faction), y, 28);
    }

    private void BuildSectorPanel(ST14GalaxySectorPrototype sector)
    {
        ClearPanel();

        AddText(Loc.GetString(sector.Name).ToUpperInvariant(), ST14HubFont.Black, 28, ST14HubStyle.GoldLight, PanelPad, 44);
        AddText(Loc.GetString("st14-galaxy-sector-label").ToUpperInvariant(), ST14HubFont.Regular, 16, ST14HubStyle.Gold, 78, 26);
        AddDivider(114);
        AddFaction(sector.Faction, 132);

        var planets = _map.Planets.Where(p => p.Sector == sector.ID).ToList();
        AddText(Loc.GetString("st14-galaxy-sector-planets", ("count", planets.Count)).ToUpperInvariant(), ST14HubFont.Regular, 16,
            new Color(0.85f, 0.88f, 0.92f), 172, 26);

        // only the war planet can be opened, the rest is listed as out of reach
        var y = 214f;
        foreach (var planet in planets.OrderByDescending(p => p.War).Take(7))
        {
            var target = planet;
            var button = AddButton(Loc.GetString(planet.Name), y, planet.War ? ST14HubStyle.Orange : null, disabled: !planet.War);
            if (planet.War)
                button.OnPressed += _ => Dive(target);
            else
                button.ToolTip = Loc.GetString("st14-galaxy-planet-locked");
            y += 76;
        }

        if (planets.Count == 0)
            AddText(Wrap(Loc.GetString("st14-galaxy-sector-empty"), 40), ST14HubFont.Regular, 15, new Color(0.7f, 0.74f, 0.8f), y, 120);
        else if (!planets.Any(p => p.War))
            AddText(Wrap(Loc.GetString("st14-galaxy-planet-locked"), 40), ST14HubFont.Regular, 15, new Color(0.7f, 0.74f, 0.8f), y, 120);

        FinishPanel();
    }

    private void BuildSurfacePanel(int? cellIndex)
    {
        if (_planet == null || WarOf(_planet) is not { } war)
            return;

        ClearPanel();

        if (cellIndex is not { } index)
        {
            AddText(Loc.GetString(_planet.Name).ToUpperInvariant(), ST14HubFont.Black, 30, ST14HubStyle.GoldLight, PanelPad, 46);
            AddText(Loc.GetString("st14-galaxy-surface-label", ("count", war.Cells.Length)).ToUpperInvariant(), ST14HubFont.Regular, 16,
                ST14HubStyle.Gold, 80, 26);
            AddDivider(114);
            AddBar(war.Liberation, 134);

            AddText(Loc.GetString("st14-galaxy-surface-bugs", ("count", war.CountOf(ST14GalaxyFaction.Arachnid))).ToUpperInvariant(),
                ST14HubFont.Medium, 17, ST14GalaxyColors.Arachnid, 190, 28);
            AddText(Loc.GetString("st14-galaxy-surface-available", ("count", war.Cells.Count(c => c.Front))).ToUpperInvariant(),
                ST14HubFont.Medium, 17, ST14HubStyle.GoldLight, 226, 28);
            AddText(Loc.GetString("st14-galaxy-surface-hives", ("count", war.Cells.Count(c => c.Hive))).ToUpperInvariant(),
                ST14HubFont.Regular, 16, new Color(1f, 0.45f, 0.3f), 268, 26);

            if (_planet.Description is { } description)
                AddText(Wrap(Loc.GetString(description), 44), ST14HubFont.Regular, 14, new Color(0.82f, 0.85f, 0.9f), 316, 90);

            AddText(Wrap(Loc.GetString("st14-galaxy-surface-help"), 44), ST14HubFont.Regular, 14, new Color(0.82f, 0.85f, 0.9f), 420, 160);

            if (war.FirstLanding is { } landing)
            {
                var go = AddButton(Loc.GetString("st14-galaxy-to-landing"), 712, ST14HubStyle.Orange);
                go.OnPressed += _ =>
                {
                    _surface.FaceCell(landing);
                    _surface.SelectedCell = landing;
                    BuildSurfacePanel(landing);
                };
            }

            FinishPanel();
            return;
        }

        var cell = war.Cells[index];
        AddText(Loc.GetString("st14-galaxy-cell-name", ("code", cell.Code)).ToUpperInvariant(), ST14HubFont.Black, 28,
            ST14HubStyle.GoldLight, PanelPad, 44);
        AddText(Loc.GetString(_planet.Name).ToUpperInvariant(), ST14HubFont.Regular, 16, ST14HubStyle.Gold, 78, 26);
        AddDivider(114);
        AddText(Loc.GetString(cell.Front ? "st14-galaxy-cell-available" : "st14-galaxy-control",
                ("faction", FactionName(cell.Faction))).ToUpperInvariant(), ST14HubFont.Medium, 17,
            cell.Front ? ST14HubStyle.GoldLight : ST14GalaxyColors.Of(cell.Faction), 132, 28);
        AddBar(cell.Liberation, 170);
        AddText(Loc.GetString("st14-galaxy-cell-terrain", ("terrain", Loc.GetString($"st14-galaxy-terrain-{cell.Terrain + 1}")))
            .ToUpperInvariant(), ST14HubFont.Regular, 16, new Color(0.85f, 0.88f, 0.92f), 222, 26);

        var y = 258f;
        if (cell.Hive)
        {
            AddText(Loc.GetString("st14-galaxy-cell-hive").ToUpperInvariant(), ST14HubFont.Medium, 16, new Color(1f, 0.45f, 0.3f), y, 26);
            y += 34;
        }

        if (cell.Base)
        {
            AddText(Loc.GetString("st14-galaxy-cell-base").ToUpperInvariant(), ST14HubFont.Medium, 16, ST14GalaxyColors.Federation, y, 26);
            y += 34;
        }

        var status = cell.Faction == ST14GalaxyFaction.Federation ? "st14-galaxy-cell-held"
            : cell.Front ? "st14-galaxy-cell-front"
            : "st14-galaxy-cell-deep";
        AddText(Wrap(Loc.GetString(status), 44), ST14HubFont.Regular, 14, new Color(0.82f, 0.85f, 0.9f), y + 10, 160);

        if (cell.Front)
        {
            var land = AddButton(Loc.GetString("st14-galaxy-land"), 712, ST14HubStyle.Orange);
            land.OnPressed += _ => OnLanding?.Invoke();
        }
        else
        {
            AddButton(Loc.GetString(cell.Faction == ST14GalaxyFaction.Federation ? "st14-galaxy-land-held" : "st14-galaxy-land-no-path"),
                712, null, disabled: true);
        }

        var back = AddButton(Loc.GetString("st14-galaxy-cell-back"), 636);
        back.OnPressed += _ =>
        {
            _surface.SelectedCell = null;
            BuildSurfacePanel(null);
        };

        FinishPanel();
    }

    /// <summary>
    /// Breaks the text into lines of at most the given length, labels do not wrap on their own
    /// </summary>
    private static string Wrap(string text, int width)
    {
        var result = new StringBuilder();
        var line = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line > 0 && line + 1 + word.Length > width)
            {
                result.Append('\n');
                line = 0;
            }
            else if (line > 0)
            {
                result.Append(' ');
                line++;
            }

            result.Append(word);
            line += word.Length;
        }

        return result.ToString();
    }

    #endregion
}

/// <summary>
/// Liberation bar: the Federation part in blue from the left, the rest belongs to the bugs
/// </summary>
public sealed class ST14GalaxyBar : Control
{
    private readonly IResourceCache _cache;

    public float Fraction;
    public string Text = string.Empty;

    public ST14GalaxyBar(IResourceCache cache)
    {
        _cache = cache;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var split = size.X * Math.Clamp(Fraction, 0, 1);
        handle.DrawRect(new UIBox2(0, 0, size.X, size.Y), ST14GalaxyColors.Arachnid.WithAlpha(0.55f));
        handle.DrawRect(new UIBox2(0, 0, split, size.Y), ST14GalaxyColors.Federation.WithAlpha(0.85f));
        handle.DrawRect(new UIBox2(0, 0, size.X, size.Y), ST14HubStyle.Gold.WithAlpha(0.6f), false);

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Medium, Math.Max(8, (int) MathF.Round(size.Y * 0.5f)));
        ST14HubStyle.DrawRotatedText(handle, font, Text, size / 2, 0, Color.White, ST14HubStyle.TextShadow);
    }
}
