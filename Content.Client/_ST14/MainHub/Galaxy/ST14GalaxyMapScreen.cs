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
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private const float PanelInset = 0.28f;
    private const float DiveTime = 0.9f;
    private const float GlobeZoom = 0.8f;
    private static readonly Vector2 PanelSize = new(520, 800);
    private const float PanelPad = 30;
    private static readonly Vector2 ToolbarSize = new(340, 800);

    private readonly ST14GalaxyTextures _textures = new();
    private readonly Dictionary<string, ST14PlanetWar> _wars = new();
    private readonly Dictionary<string, int> _targets = new();
    private readonly ST14GalaxyMapView _map;
    private readonly ST14PlanetGlobe _surface;
    private readonly PanelContainer _panelBack;
    private readonly ST14CanvasLayout _panel;
    private readonly Label _path;
    private readonly Label _stats;
    private readonly Label _hint;
    private readonly Dictionary<ST14GalaxyFaction, Label> _orderLines = new();
    private readonly ST14HubButton _commandButton;
    private readonly PanelContainer _toolbarBack;
    private readonly ST14CanvasLayout _toolbar;

    private ST14GalaxyLevel _level;
    private ST14GalaxySectorPrototype? _sector;
    private ST14GalaxyPlanetPrototype? _planet;
    private float _dive;
    private bool _canCommand;
    private readonly HashSet<ST14GalaxyFaction> _commandSides = new();
    private ST14GalaxyFaction _commandSide = ST14GalaxyFaction.Federation;
    private bool _listening;
    private bool _commandMode;
    // unsent order text of each command, so switching sides never carries one side's order into the other
    private readonly Dictionary<ST14GalaxyFaction, string> _orderDrafts = new();

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
        _map.OnMarkPlaced += mark => WarSystem?.AddMark(_commandSide, mark);
        _map.OnMarkErased += (side, index) => WarSystem?.RemoveMark(side, index);
        _map.OnToolCancelled += () => SetTool(null, false);
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

        // the standing orders of the commands, each side reads its own
        var orderY = 132f;
        foreach (var side in ST14GalaxyWarRules.Sides)
        {
            var line = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Medium, 18,
                side == ST14GalaxyFaction.Arachnid ? ST14GalaxyColors.Arachnid : ST14HubStyle.Orange, shadow: true);
            line.Visible = false;
            Canvas.AddAt(line, Vector2.Zero, new Vector2(62, orderY), new Vector2(1300, 30), Vector2.Zero);
            _orderLines[side] = line;
            orderY += 30;
        }

        // the planning table of the High Command, only for its members
        _commandButton = new ST14HubButton(ResCache, Loc.GetString("st14-galaxy-command"), 18, ST14HubStyle.Blue, ST14HubStyle.BlueText)
        {
            Visible = false,
        };
        _commandButton.OnPressed += _ => SetCommandMode(!_commandMode);
        Canvas.AddAt(_commandButton, new Vector2(1, 0), new Vector2(-60, 120), new Vector2(380, 60), new Vector2(1, 0));

        _toolbarBack = new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(new Color(0.02f, 0.035f, 0.06f, 0.86f), ST14HubStyle.Gold.WithAlpha(0.45f), 1.5f),
            MouseFilter = MouseFilterMode.Stop,
            Visible = false,
        };
        Canvas.AddAt(_toolbarBack, new Vector2(0, 0.5f), new Vector2(40, 30), ToolbarSize, new Vector2(0, 0.5f));
        _toolbar = new ST14CanvasLayout { Visible = false };
        Canvas.AddAt(_toolbar, new Vector2(0, 0.5f), new Vector2(40, 30), ToolbarSize, new Vector2(0, 0.5f));

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

        // the map opens on the whole galaxy every time and asks the server for the war as it is now
        if (newVisible)
        {
            ShowGalaxy();
            if (WarSystem is not { } system)
                return;

            if (!_listening)
            {
                system.StateReceived += OnWarState;
                system.OrdersReceived += OnOrders;
                _listening = true;
            }

            foreach (var orders in system.Orders.Values)
            {
                OnOrders(orders);
            }

            system.Request();
        }
        else if (_listening && WarSystem is { } system)
        {
            system.StateReceived -= OnWarState;
            system.OrdersReceived -= OnOrders;
            _listening = false;
            SetCommandMode(false);
        }
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

    /// <summary>
    /// Entity systems only live while the client is connected, the map draws the war made from the seed without them
    /// </summary>
    private ST14GalaxyWarClientSystem? WarSystem =>
        _entMan.EntitySysManager.TryGetEntitySystem<ST14GalaxyWarClientSystem>(out var system) ? system : null;

    private void OnWarState(ST14GalaxyWarStateEvent ev)
    {
        _canCommand = ev.CanCommand;
        UpdateCommand();
        _targets[ev.Planet] = ev.Target;
        if (_wars.TryGetValue(ev.Planet, out var war))
            war.ImportState(ev.Cells);

        if (_level != ST14GalaxyLevel.Surface || _planet?.ID != ev.Planet)
            return;

        _surface.TargetCell = TargetOf(_planet);
        BuildSurfacePanel(_surface.SelectedCell);
    }

    private void OnOrders(ST14GalaxyOrdersEvent ev)
    {
        if (ev.CanCommand)
            _commandSides.Add(ev.Side);
        else
            _commandSides.Remove(ev.Side);

        if (_orderLines.TryGetValue(ev.Side, out var line))
        {
            line.Text = ev.Order.Length == 0
                ? string.Empty
                : Loc.GetString($"st14-galaxy-order-line-{SideKey(ev.Side)}", ("order", ev.Order)).ToUpperInvariant();
            line.Visible = ev.Order.Length > 0;
        }

        // every plan this player may see goes on the map, each remembers its side and its index on the server
        var marks = new List<(ST14GalaxyFaction, int, ST14WarMark)>();
        if (WarSystem is { } system)
        {
            foreach (var (side, orders) in system.Orders)
            {
                for (var i = 0; i < orders.Marks.Length; i++)
                {
                    marks.Add((side, i, orders.Marks[i]));
                }
            }
        }

        _map.Marks = marks;
        UpdateCommand();
    }

    private static string SideKey(ST14GalaxyFaction side)
    {
        return side == ST14GalaxyFaction.Arachnid ? "bugs" : "federation";
    }

    #region High Command

    /// <summary>
    /// The button shows for the High Command on the galaxy and sector levels, the planet has its own orders
    /// </summary>
    private void UpdateCommand()
    {
        var available = _commandSides.Count > 0 && _level is ST14GalaxyLevel.Galaxy or ST14GalaxyLevel.Sector;
        if (_commandSides.Count > 0 && !_commandSides.Contains(_commandSide))
            _commandSide = _commandSides.First();

        _map.ToolSide = _commandSide;
        _commandButton.Visible = available;
        if (!available && _commandMode)
            SetCommandMode(false);
    }

    private void SetCommandMode(bool on)
    {
        _commandMode = on;
        _toolbarBack.Visible = on;
        _toolbar.Visible = on;
        _commandButton.Text = Loc.GetString(on ? "st14-galaxy-command-close" : "st14-galaxy-command");
        if (on)
            BuildToolbar();
        else
        {
            // a closed panel forgets what was typed and not sent
            _orderDrafts.Clear();
            SetTool(null, false);
        }
    }

    private void SetTool(ST14WarMarkKind? tool, bool eraser)
    {
        _map.Tool = tool;
        _map.Eraser = eraser;
        if (_commandMode)
            BuildToolbar();
    }

    private void SendOrder(ST14GalaxyFaction side, string order)
    {
        WarSystem?.SetOrder(side, order);
        // sent: the order now shows on the map, the draft is done
        _orderDrafts.Remove(side);
    }

    private void BuildToolbar()
    {
        _toolbar.RemoveAllChildren();

        var title = ST14HubStyle.Label(ResCache, Loc.GetString($"st14-galaxy-command-{SideKey(_commandSide)}").ToUpperInvariant(),
            ST14HubFont.Black, 18, _commandSide == ST14GalaxyFaction.Arachnid ? ST14GalaxyColors.Arachnid : ST14HubStyle.GoldLight, shadow: true);
        _toolbar.AddAt(title, Vector2.Zero, new Vector2(24, 20), new Vector2(300, 32), Vector2.Zero);

        var y = 66f;

        // someone in both commands, like an admin, switches between the plans
        if (_commandSides.Count > 1)
        {
            var x = 24f;
            foreach (var side in ST14GalaxyWarRules.Sides)
            {
                var target = side;
                var pick = new ST14HubButton(ResCache, Loc.GetString($"st14-galaxy-side-{SideKey(side)}"), 14,
                    side == _commandSide ? ST14HubStyle.Orange : null);
                pick.OnPressed += _ =>
                {
                    _commandSide = target;
                    _map.ToolSide = target;
                    SetTool(null, false);
                };
                _toolbar.AddAt(pick, Vector2.Zero, new Vector2(x, y), new Vector2(142, 46), Vector2.Zero);
                x += 150;
            }

            y += 56;
        }
        foreach (var kind in Enum.GetValues<ST14WarMarkKind>())
        {
            var tool = kind;
            var active = _map.Tool == kind;
            var button = new ST14HubButton(ResCache, Loc.GetString($"st14-galaxy-tool-{kind.ToString().ToLowerInvariant()}"), 16,
                active ? ST14HubStyle.Orange : null);
            button.OnPressed += _ => SetTool(active ? null : tool, false);
            _toolbar.AddAt(button, Vector2.Zero, new Vector2(ToolbarSize.X / 2, y), new Vector2(290, 54), new Vector2(0.5f, 0));
            y += 62;
        }

        var eraser = new ST14HubButton(ResCache, Loc.GetString("st14-galaxy-tool-eraser"), 16, _map.Eraser ? ST14HubStyle.Orange : null);
        eraser.OnPressed += _ => SetTool(null, !_map.Eraser);
        _toolbar.AddAt(eraser, Vector2.Zero, new Vector2(ToolbarSize.X / 2, y), new Vector2(290, 54), new Vector2(0.5f, 0));
        y += 62;

        var clear = new ST14HubButton(ResCache, Loc.GetString("st14-galaxy-tool-clear"), 16, Color.FromHex("#C0503A"));
        clear.OnPressed += _ => WarSystem?.RemoveMark(_commandSide, -1);
        _toolbar.AddAt(clear, Vector2.Zero, new Vector2(ToolbarSize.X / 2, y), new Vector2(290, 54), new Vector2(0.5f, 0));
        y += 74;

        var orderTitle = ST14HubStyle.Label(ResCache, Loc.GetString("st14-galaxy-order-title").ToUpperInvariant(), ST14HubFont.Medium, 15,
            ST14HubStyle.Gold, shadow: true);
        _toolbar.AddAt(orderTitle, Vector2.Zero, new Vector2(24, y), new Vector2(300, 26), Vector2.Zero);
        y += 30;

        // the panel is rebuilt for the side picked now, the input and the button stay bound to it
        var orderSide = _commandSide;
        var input = new LineEdit
        {
            Text = _orderDrafts.GetValueOrDefault(orderSide, string.Empty),
            PlaceHolder = Loc.GetString("st14-galaxy-order-placeholder"),
        };
        input.OnTextChanged += args => _orderDrafts[orderSide] = args.Text;
        input.OnTextEntered += args => SendOrder(orderSide, args.Text);
        _toolbar.AddAt(input, Vector2.Zero, new Vector2(24, y), new Vector2(292, 40), Vector2.Zero);
        y += 50;

        var send = new ST14HubButton(ResCache, Loc.GetString("st14-galaxy-order-send"), 16, ST14HubStyle.Blue, ST14HubStyle.BlueText);
        send.OnPressed += _ => SendOrder(orderSide, input.Text);
        _toolbar.AddAt(send, Vector2.Zero, new Vector2(ToolbarSize.X / 2, y), new Vector2(290, 54), new Vector2(0.5f, 0));

        ST14HubStyle.ScaleLabels(_toolbar, Canvas.CanvasScale);
        _hint.Text = Loc.GetString(_map.Tool != null || _map.Eraser ? "st14-galaxy-hint-drawing" : "st14-galaxy-hint-command")
            .ToUpperInvariant();
    }

    #endregion

    /// <summary>
    /// Sector the High Command sent the troopers to, null while the server has not said
    /// </summary>
    private int? TargetOf(ST14GalaxyPlanetPrototype planet)
    {
        return _targets.TryGetValue(planet.ID, out var target) && target >= 0 ? target : null;
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
        UpdateCommand();
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
        UpdateCommand();
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
        UpdateCommand();
    }

    private void ShowSurface(ST14GalaxyPlanetPrototype planet)
    {
        _level = ST14GalaxyLevel.Surface;
        _planet = planet;
        ShowMap(false);
        _surface.SetPlanet(planet, WarOf(planet));
        _surface.TargetCell = TargetOf(planet);
        if (TargetOf(planet) is { } target)
            _surface.FaceCell(target);
        _surface.Intro(0.12f, GlobeZoom);
        SetPath(_sector, planet, true);
        _hint.Text = Loc.GetString("st14-galaxy-hint-surface").ToUpperInvariant();
        UpdateCommand();
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
        if (WarSystem?.TryGetState(planet.ID, out var state) == true)
        {
            war.ImportState(state.Cells);
            _targets[planet.ID] = state.Target;
        }

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

    /// <summary>
    /// Dead of the rounds fought there: bugs on the left, troopers on the right
    /// </summary>
    private void AddKills(int bugs, int troopers, float y)
    {
        var half = (PanelSize.X - PanelPad * 2) / 2;
        var bugLabel = ST14HubStyle.Label(ResCache, Loc.GetString("st14-galaxy-kills-bugs", ("count", bugs)).ToUpperInvariant(),
            ST14HubFont.Medium, 15, ST14GalaxyColors.Arachnid, shadow: true);
        _panel.AddAt(bugLabel, Vector2.Zero, new Vector2(PanelPad, y), new Vector2(half, 26), Vector2.Zero);

        var trooperLabel = ST14HubStyle.Label(ResCache, Loc.GetString("st14-galaxy-kills-troopers", ("count", troopers)).ToUpperInvariant(),
            ST14HubFont.Medium, 15, ST14GalaxyColors.Federation, shadow: true);
        trooperLabel.Align = Label.AlignMode.Right;
        _panel.AddAt(trooperLabel, Vector2.Zero, new Vector2(PanelPad + half, y), new Vector2(half, 26), Vector2.Zero);
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
                ST14HubFont.Regular, 16, new Color(1f, 0.45f, 0.3f), 262, 26);
            AddKills(war.BugsKilled, war.TroopersKilled, 290);

            if (_planet.Description is { } description)
                AddText(Wrap(Loc.GetString(description), 44), ST14HubFont.Regular, 14, new Color(0.82f, 0.85f, 0.9f), 336, 80);

            AddText(Wrap(Loc.GetString("st14-galaxy-surface-help"), 44), ST14HubFont.Regular, 14, new Color(0.82f, 0.85f, 0.9f), 420, 160);

            if ((TargetOf(_planet) ?? war.FirstLanding) is { } landing)
            {
                AddText(Loc.GetString("st14-galaxy-target", ("code", war.Cells[landing].Code)).ToUpperInvariant(), ST14HubFont.Medium, 16,
                    ST14HubStyle.GoldLight, 650, 26);
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

        AddKills(cell.BugsKilled, cell.TroopersKilled, 252);

        var y = 286f;
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

        // troopers land where the High Command sends them, the High Command can move the attack along the front
        var target = TargetOf(_planet);
        if (cell.Front && (target == null || target == index))
        {
            if (target == index)
                AddText(Loc.GetString("st14-galaxy-cell-target").ToUpperInvariant(), ST14HubFont.Medium, 16, ST14HubStyle.GoldLight, 590, 26);

            var land = AddButton(Loc.GetString("st14-galaxy-land"), 712, ST14HubStyle.Orange);
            land.OnPressed += _ => OnLanding?.Invoke();
        }
        else if (cell.Front && _canCommand)
        {
            var planetId = _planet.ID;
            var order = AddButton(Loc.GetString("st14-galaxy-order"), 712, ST14HubStyle.Blue);
            order.OnPressed += _ => WarSystem?.Order(planetId, index);
        }
        else if (cell.Front)
        {
            AddButton(Loc.GetString("st14-galaxy-land-not-target"), 712, null, disabled: true);
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
