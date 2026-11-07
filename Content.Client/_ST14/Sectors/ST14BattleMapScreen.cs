using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The map of the war a player opens in a round, over the whole screen like the map of Foxhole and in the brass of the
/// table of the High Command. From afar it is the planet with every sector, close up the flat map of the battle: the
/// wheel goes from one to the other. On the left what is known of a sector, on the right the squad and the plan
/// </summary>
public sealed class ST14BattleMapScreen : ST14WarScreenBase
{
    private const float WindowY = 124;
    private const float WindowHeight = 846;
    private const float LeftX = 62;
    private const float RightX = 1462;
    private const float InnerWidth = 396;

    private static readonly (ST14WarMarkKind? Kind, string Key, string Icon)[] Tools =
    {
        (ST14WarMarkKind.Attack, "attack", "icon_attack.png"),
        (ST14WarMarkKind.Defend, "defend", "icon_defend.png"),
        (ST14WarMarkKind.Rally, "rally", "icon_rally.png"),
        (ST14WarMarkKind.Danger, "danger", "icon_danger.png"),
        (ST14WarMarkKind.Objective, "objective", "icon_objective.png"),
        (null, "eraser", "icon_erase.png"),
    };

    private readonly IPrototypeManager _prototypes;
    private readonly ST14BattleMapClientSystem _system;
    private readonly ST14GalaxyWarClientSystem _war;
    private readonly ST14PlanetGlobe _globe;
    private readonly ST14BattleMapView _flat;
    private readonly ST14CanvasLayout _header = new();
    private readonly ST14CanvasLayout _left = new();
    private readonly ST14CanvasLayout _right = new();
    private readonly Label _hint;

    private ST14GalaxyPlanetPrototype? _planet;
    private ST14PlanetWar? _planetWar;
    private bool _battleMode;
    private int? _selected;
    private int? _hovered;
    private ST14WarMarkKind? _tool;
    private bool _eraser;

    // the panels are built again only when what they show changed: the map gets news every second
    private string _headerKey = string.Empty;
    private string _leftKey = string.Empty;
    private string _rightKey = string.Empty;

    public ST14BattleMapScreen(ST14BattleMapClientSystem system, ST14GalaxyWarClientSystem war, SpriteSystem sprite, IPrototypeManager prototypes)
        : base("ST14HubGalaxyMap")
    {
        _system = system;
        _war = war;
        _prototypes = prototypes;

        _globe = new ST14PlanetGlobe(new ST14GalaxyTextures())
        {
            Zoom = 0.9f,
            MinZoom = 0.7f,
            MaxZoom = 1.7f,
            AutoSpin = false,
            TagCache = ResCache,
        };
        _globe.OnCellSelected += cell => Select(cell);
        _globe.OnCellHovered += cell => Hover(cell);
        _globe.OnZoomPastMax += () =>
        {
            if ((_hovered ?? _selected) is { } cell && IsLoaded(cell))
                ShowBattle(cell);
        };
        _globe.OnRightClicked += () => SetTool(null, false);
        _globe.OnMarkPlaced += mark => _war.AddMark(_system.State?.Side ?? ST14GalaxyFaction.Federation, mark);
        _globe.OnMarkErased += (side, index) => _war.RemoveMark(side, index);
        Canvas.AddAt(_globe, Vector2.Zero, new Vector2(480, 96), new Vector2(960, 892), Vector2.Zero);

        _flat = new ST14BattleMapView(ResCache, system, sprite) { Visible = false };
        _flat.OnZoomPastMin += cell => ShowPlanet(cell);
        _flat.OnCellHovered += cell => Hover(cell);
        _flat.OnCellClicked += cell => Select(cell);
        _flat.OnToolCancelled += () => SetTool(null, false);
        _flat.OnMarkPlaced += mark =>
        {
            if (_system.State is { } state)
                _system.AddMark(state.Side, mark);
        };
        _flat.OnMarkErased += index =>
        {
            if (_system.State is { } state)
                _system.RemoveMark(state.Side, index);
        };
        Canvas.AddAt(_flat, Vector2.Zero, new Vector2(488, 104), new Vector2(944, 876), Vector2.Zero);

        var box = ST14HubStyle.Window(ResCache);
        Canvas.AddAt(new PanelContainer { PanelOverride = box, MouseFilter = MouseFilterMode.Pass }, Vector2.Zero, new Vector2(40, WindowY), new Vector2(440, WindowHeight), Vector2.Zero);
        Canvas.AddAt(new PanelContainer { PanelOverride = box, MouseFilter = MouseFilterMode.Pass }, Vector2.Zero, new Vector2(1440, WindowY), new Vector2(440, WindowHeight), Vector2.Zero);
        Canvas.AddFull(_header);
        Canvas.AddFull(_left);
        Canvas.AddFull(_right);

        AddBottomBar();
        _hint = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Regular, 19, ST14HubStyle.Gold, shadow: true);
        _hint.Align = Label.AlignMode.Center;
        _hint.VAlign = Label.VAlignMode.Center;
        Canvas.AddAt(_hint, new Vector2(0.5f, 1), new Vector2(0, -46.5f), new Vector2(1300, 50));

        _system.Changed += Refresh;
        _system.SquadsChanged += Refresh;
        _war.StateReceived += _ => Refresh();
        _war.OrdersReceived += _ => Refresh();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _system.Changed -= Refresh;
        _system.SquadsChanged -= Refresh;
    }

    public void Open()
    {
        Visible = true;
        _battleMode = false;
        _selected = null;
        _hovered = null;
        _headerKey = _leftKey = _rightKey = string.Empty;
        SetTool(null, false);
        _system.Request();
        _system.RequestSquads();
        _war.Request();
        Refresh();
    }

    public void Close()
    {
        if (!Visible)
            return;

        Visible = false;
        SetTool(null, false);
        _system.Closed();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // the clock of the battle in the header runs by itself
        if (Visible && _system.State is { Active: true })
            UpdateHeaderClock();
    }

    #region State

    private bool IsLoaded(int cell)
    {
        return _system.State?.Sectors.Any(s => s.Cell == cell && s.Loaded) == true;
    }

    private ST14PlanetWar? War()
    {
        if (_system.State is not { Active: true } state || !_prototypes.TryIndex<ST14GalaxyPlanetPrototype>(state.Planet, out var planet))
            return null;

        if (_planet?.ID != planet.ID || _planetWar == null)
        {
            _planet = planet;
            var name = Loc.GetString(planet.Name);
            _planetWar = new ST14PlanetWar(planet, name.Length > 0 ? name[..1].ToUpperInvariant() : "X");
            _globe.SetPlanet(planet, _planetWar);
        }

        if (_war.TryGetState(planet.ID, out var warState))
            _planetWar.ImportState(warState.Cells, warState.Turn, warState.ActiveRegion);

        return _planetWar;
    }

    private void Select(int? cell)
    {
        _selected = cell;
        Refresh();
    }

    private void Hover(int? cell)
    {
        _hovered = cell;
        Refresh();
    }

    private void ShowBattle(int cell)
    {
        _battleMode = true;
        _flat.Visible = true;
        _globe.Visible = false;
        _flat.Focus(cell);
        _selected = cell;
        Refresh();
    }

    private void ShowPlanet(int? cell)
    {
        _battleMode = false;
        _flat.Visible = false;
        _globe.Visible = true;
        _globe.Zoom = _globe.MaxZoom * 0.92f;
        if (cell is { } face)
            _globe.FaceCellSmooth(face);

        Refresh();
    }

    private void SetTool(ST14WarMarkKind? tool, bool eraser)
    {
        _tool = tool;
        _eraser = eraser;
        _flat.Tool = _battleMode ? tool : null;
        _flat.Eraser = _battleMode && eraser;
        _globe.Tool = _battleMode ? null : tool;
        _globe.Eraser = !_battleMode && eraser;
        _rightKey = string.Empty;
        Refresh();
    }

    protected override void GoBack()
    {
        if (_tool != null || _eraser)
        {
            SetTool(null, false);
            return;
        }

        base.GoBack();
    }

    private void Refresh()
    {
        if (!Visible)
            return;

        var state = _system.State;
        var war = War();
        UpdateGlobe(state, war);
        BuildHeader(state, war);
        BuildLeft(state, war);
        BuildRight(state);

        _hint.Text = Loc.GetString(state is not { Active: true } ? "st14-battle-map-inactive"
            : _battleMode ? "st14-battle-map-hint-battle" : "st14-battle-map-hint-planet").ToUpperInvariant();
    }

    private void UpdateGlobe(ST14BattleMapStateEvent? state, ST14PlanetWar? war)
    {
        _globe.Tags.Clear();
        if (state is not { Active: true } || war == null)
            return;

        _globe.TargetCell = state.Target >= 0 ? state.Target : null;
        _globe.StagingCell = state.Staging >= 0 ? state.Staging : null;
        _globe.SelectedCell = _selected;

        // the sectors of this battle are named on the planet, the one of the player first
        foreach (var sector in state.Sectors.Where(s => s.Loaded))
        {
            var key = sector.Cell == state.Here ? "st14-battle-map-tag-here"
                : sector.Cell == state.Target ? "st14-battle-map-tag-target"
                : sector.Cell == state.Staging ? "st14-battle-map-tag-staging"
                : "st14-battle-map-tag-battle";
            var color = sector.Cell == state.Here ? Color.White : ST14HubStyle.GoldLight;
            _globe.Tags.Add((sector.Cell, $"{sector.Code} · {Loc.GetString(key)}".ToUpperInvariant(), color));
        }

        // the plan of the command for the whole war, the one of the table of the High Command
        var marks = new List<(ST14GalaxyFaction Side, int Index, ST14WarMark Mark)>();
        if (_war.Orders.TryGetValue(state.Side, out var orders))
        {
            for (var i = 0; i < orders.Marks.Length; i++)
            {
                if (orders.Marks[i].Surface)
                    marks.Add((state.Side, i, orders.Marks[i]));
            }
        }

        _globe.Marks = marks;
        _globe.ToolSide = state.Side;
    }

    #endregion

    #region Panels

    private Label AddLabel(ST14CanvasLayout canvas, string text, ST14HubFont font, int size, Color color, float x, float y, float width, float height)
    {
        var label = ST14HubStyle.Label(ResCache, text, font, size, color, shadow: true);
        label.ClipText = true;
        canvas.AddAt(label, Vector2.Zero, new Vector2(x, y), new Vector2(width, height), Vector2.Zero);
        return label;
    }

    private void AddTitle(ST14CanvasLayout canvas, string key, float windowX)
    {
        // engraved into the brass of the window
        AddLabel(canvas, Loc.GetString(key).ToUpperInvariant(), ST14HubFont.Bold, 18, ST14HqArt.Dark, windowX + 46, WindowY + 6, 340, 36);
    }

    private ST14HubButton AddPlate(ST14CanvasLayout canvas, string text, Color? tint, float x, float y, float width, float height, int fontSize = 17, bool disabled = false)
    {
        var button = new ST14HubButton(ResCache, text, fontSize, disabled ? Color.FromHex("#8A8A8A") : tint)
        {
            Disabled = disabled,
            MouseFilter = MouseFilterMode.Pass,
        };
        canvas.AddAt(button, Vector2.Zero, new Vector2(x, y), new Vector2(width, height), Vector2.Zero);
        return button;
    }

    private static string SideKey(ST14GalaxyFaction side)
    {
        return side == ST14GalaxyFaction.Arachnid ? "bugs" : "federation";
    }

    private static string FactionName(ST14GalaxyFaction faction)
    {
        return Loc.GetString($"st14-galaxy-faction-{faction.ToString().ToLowerInvariant()}");
    }

    private Label? _clock;

    private void UpdateHeaderClock()
    {
        if (_clock == null || _system.State is not { } state || state.Target < 0 || _planetWar == null || _planet == null)
            return;

        var code = _planetWar.Cells[state.Target].Code;
        var planet = Loc.GetString(_planet.Name);
        var left = _war.TryGetState(_planet.ID, out var warState) && warState.Battle
            ? TimeSpan.FromSeconds(MathF.Max(0, warState.BattleSecondsLeft - (float) (DateTime.UtcNow - _clockFrom).TotalSeconds))
            : (TimeSpan?) null;
        _clock.Text = (left is { } time
            ? Loc.GetString("st14-battle-map-path", ("planet", planet), ("code", code), ("time", $"{(int) time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"))
            : Loc.GetString("st14-battle-map-path-idle", ("planet", planet), ("code", code))).ToUpperInvariant();
    }

    private DateTime _clockFrom = DateTime.UtcNow;
    private float _clockSeconds = -1;

    private void BuildHeader(ST14BattleMapStateEvent? state, ST14PlanetWar? war)
    {
        var side = state?.Side ?? ST14GalaxyFaction.Federation;
        var federation = war?.CountOf(ST14GalaxyFaction.Federation) ?? 0;
        var key = $"{state?.Active}|{side}|{_battleMode}|{federation}|{war?.Cells.Length}";
        if (_planet != null && _war.TryGetState(_planet.ID, out var warState) && warState.BattleSecondsLeft != _clockSeconds)
        {
            // the server tells the time left now and then, the clock counts down from the last word
            _clockSeconds = warState.BattleSecondsLeft;
            _clockFrom = DateTime.UtcNow;
        }

        if (key == _headerKey)
            return;

        _headerKey = key;
        _header.RemoveAllChildren();

        var emblem = new ST14HqPortrait { Portrait = ST14HqArt.Tex(ResCache, side == ST14GalaxyFaction.Arachnid ? "emblem_hive.png" : "emblem_federation.png") };
        _header.AddAt(emblem, Vector2.Zero, new Vector2(40, 16), new Vector2(96, 96), Vector2.Zero);
        AddLabel(_header, Loc.GetString("st14-battle-map-title").ToUpperInvariant(), ST14HubFont.Black, 40, ST14HubStyle.GoldLight, 148, 18, 620, 58);
        _clock = AddLabel(_header, string.Empty, ST14HubFont.Regular, 19, ST14HubStyle.Gold, 150, 74, 620, 30);
        UpdateHeaderClock();

        if (war != null)
        {
            var score = new ST14HqScore(ResCache)
            {
                Federation = federation,
                Hive = war.Cells.Length - federation,
                FederationName = FactionName(ST14GalaxyFaction.Federation).ToUpperInvariant(),
                HiveName = FactionName(ST14GalaxyFaction.Arachnid).ToUpperInvariant(),
            };
            _header.AddAt(score, Vector2.Zero, new Vector2(780, 20), new Vector2(620, 42), Vector2.Zero);
        }

        if (state is not { Active: true })
            return;

        var planet = AddPlate(_header, Loc.GetString("st14-battle-map-mode-planet"), _battleMode ? ST14HqArt.PlateOff : ST14HubStyle.Blue, 1440, 26, 216, 62, 18);
        planet.OnPressed += _ => ShowPlanet(_selected);
        var battleCell = _selected is { } chosen && IsLoaded(chosen) ? chosen : state.Here >= 0 ? state.Here : state.Target;
        var battle = AddPlate(_header, Loc.GetString("st14-battle-map-mode-battle"), _battleMode ? ST14HubStyle.Blue : ST14HqArt.PlateOff, 1664, 26, 216, 62, 18);
        battle.OnPressed += _ => ShowBattle(battleCell);
    }

    private void AddFact(string key, string value, Color color, ref float y)
    {
        AddLabel(_left, Loc.GetString(key).ToUpperInvariant(), ST14HubFont.Regular, 15, ST14HqArt.Muted, LeftX, y, InnerWidth, 24);
        var label = AddLabel(_left, value.ToUpperInvariant(), ST14HubFont.Medium, 15, color, LeftX, y, InnerWidth, 24);
        label.Align = Label.AlignMode.Right;
        y += 25;
    }

    private void BuildLeft(ST14BattleMapStateEvent? state, ST14PlanetWar? war)
    {
        var shown = _hovered ?? _selected ?? (state?.Here >= 0 ? state.Here : null);
        var points = _system.Live?.Points.Where(p => p.Cell == shown).ToList() ?? new List<ST14SubsectorPoint>();
        var key = $"{state?.Active}|{shown}|{_battleMode}|{(shown is { } c && war != null ? war.Cells[c].Faction : default)}|{string.Join(",", points.Select(p => p.Holder))}";
        if (key == _leftKey)
            return;

        _leftKey = key;
        _left.RemoveAllChildren();
        AddTitle(_left, "st14-battle-map-summary", 40);
        if (state is not { Active: true } || war == null)
            return;

        var y = 184f;
        if (shown is { } cell && cell >= 0 && cell < war.Cells.Length)
        {
            var hex = war.Cells[cell];
            AddLabel(_left, Loc.GetString("st14-battle-map-sector", ("code", hex.Code)).ToUpperInvariant(), ST14HubFont.Black, 30, ST14HubStyle.GoldLight, LeftX, y, InnerWidth, 44);
            y += 52;
            AddFact("st14-hq-fact-control", FactionName(hex.Faction), ST14GalaxyColors.Of(hex.Faction), ref y);
            AddFact("st14-hq-fact-terrain", Loc.GetString($"st14-galaxy-terrain-{hex.Terrain}"), Color.White, ref y);
            AddFact("st14-hq-fact-region", war.Cells[war.RegionCenters[hex.Region]].Code, Color.White, ref y);
            AddFact("st14-hq-fact-fights", Loc.GetString(hex.Initialized ? "st14-hq-fought" : "st14-hq-not-fought"), Color.White, ref y);
            AddFact("st14-battle-map-fact-losses", Loc.GetString("st14-hq-losses", ("bugs", hex.BugsKilled), ("troopers", hex.TroopersKilled)), Color.White, ref y);

            // the subsectors: whoever holds every point when the round ends takes the sector
            if (points.Count > 0)
            {
                AddFact("st14-battle-map-fact-points", Loc.GetString("st14-battle-map-points",
                    ("federation", points.Count(p => p.Holder == ST14GalaxyFaction.Federation)),
                    ("bugs", points.Count(p => p.Holder == ST14GalaxyFaction.Arachnid)), ("total", points.Count)), Color.White, ref y);
            }

            y += 12;
            var loaded = IsLoaded(cell);
            AddLabel(_left, Loc.GetString(loaded ? "st14-battle-map-loaded-yes" : "st14-battle-map-loaded-no").ToUpperInvariant(),
                ST14HubFont.SemiBold, 16, loaded ? Color.FromHex("#6FE08A") : ST14HqArt.Muted, LeftX, y, InnerWidth, 28);
            y += 36;
            if (loaded && !_battleMode)
                AddPlate(_left, Loc.GetString("st14-battle-map-open-battle"), ST14HubStyle.Orange, LeftX, y, InnerWidth, 58, 16).OnPressed += _ => ShowBattle(cell);
        }
        else
        {
            AddLabel(_left, Loc.GetString("st14-battle-map-pick").ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HqArt.Muted, LeftX, y, InnerWidth, 26);
        }

        // what the signs on the map mean
        y = 690;
        AddLabel(_left, Loc.GetString("st14-battle-map-legend").ToUpperInvariant(), ST14HubFont.SemiBold, 15, ST14HubStyle.Gold, LeftX, y, InnerWidth, 26);
        y += 30;
        var legend = new (Color Color, string Key)[]
        {
            (ST14GalaxyColors.Federation, "st14-galaxy-faction-federation"),
            (ST14GalaxyColors.Arachnid, "st14-galaxy-faction-arachnid"),
            (Color.FromHex("#6FE08A"), "st14-battle-map-legend-open"),
            (Color.FromHex("#A8A49A"), "st14-battle-map-legend-closed"),
            (Color.White, "st14-battle-map-legend-you"),
            (Color.FromHex("#5CD6FF"), "st14-battle-map-legend-mate"),
        };
        for (var i = 0; i < legend.Length; i++)
        {
            var (color, text) = legend[i];
            var x = LeftX + i % 2 * 200;
            AddLabel(_left, "●", ST14HubFont.Bold, 16, color, x, y + i / 2 * 26, 20, 24);
            AddLabel(_left, Loc.GetString(text).ToUpperInvariant(), ST14HubFont.Regular, 14, Color.White, x + 22, y + i / 2 * 26, 176, 24);
        }
    }

    private void BuildRight(ST14BattleMapStateEvent? state)
    {
        var commands = state?.Commands ?? Array.Empty<ST14GalaxyFaction>();
        var key = $"{state?.Active}|{state?.Side}|{string.Join(',', commands)}|{_tool}|{_eraser}|{_battleMode}|{state?.Marks.Length}";
        if (key == _rightKey)
            return;

        _rightKey = key;
        _right.RemoveAllChildren();
        AddTitle(_right, "st14-battle-map-squad-title", 1440);
        if (state is not { Active: true })
            return;

        var squad = new ST14SquadPanel(ResCache, _system, false) { MouseFilter = MouseFilterMode.Pass };
        var scroll = new ScrollContainer { HScrollEnabled = false };
        scroll.AddChild(squad);
        _right.AddAt(scroll, Vector2.Zero, new Vector2(RightX, 184), new Vector2(InnerWidth, 330), Vector2.Zero);

        var y = 530f;
        AddLabel(_right, Loc.GetString("st14-battle-map-plan-heading").ToUpperInvariant(), ST14HubFont.SemiBold, 16, ST14HubStyle.Gold, RightX, y, InnerWidth, 26);
        y += 32;

        // a member of both commands, like an admin, draws for one at a time
        if (commands.Length > 1)
        {
            var x = RightX;
            foreach (var side in commands)
            {
                var target = side;
                AddPlate(_right, Loc.GetString($"st14-galaxy-side-{SideKey(side)}"), side == state.Side ? ST14HubStyle.Blue : ST14HqArt.PlateOff, x, y, 194, 48, 15)
                    .OnPressed += _ =>
                    {
                        SetTool(null, false);
                        _system.Request(target);
                    };
                x += 202;
            }

            y += 56;
        }

        if (!commands.Contains(state.Side))
        {
            AddLabel(_right, Loc.GetString("st14-battle-map-plan-command-only").ToUpperInvariant(), ST14HubFont.Medium, 14, ST14HqArt.Muted, RightX, y, InnerWidth, 26);
            return;
        }

        AddLabel(_right, Loc.GetString(_battleMode ? "st14-battle-map-plan-battle" : "st14-battle-map-plan-planet").ToUpperInvariant(),
            ST14HubFont.Regular, 13, ST14HqArt.Muted, RightX, y, InnerWidth, 22);
        y += 26;
        for (var i = 0; i < Tools.Length; i++)
        {
            var (kind, toolKey, icon) = Tools[i];
            var active = kind != null ? _tool == kind : _eraser;
            var tile = new ST14HqTile(ResCache, Loc.GetString($"st14-galaxy-tile-{toolKey}").ToUpperInvariant(), ST14HqArt.Tex(ResCache, icon), active);
            tile.OnPressed += _ =>
            {
                if (kind != null)
                    SetTool(active ? null : kind, false);
                else
                    SetTool(null, !active);
            };
            _right.AddAt(tile, Vector2.Zero, new Vector2(RightX + i % 3 * 134, y + i / 3 * 104), new Vector2(128, 98), Vector2.Zero);
        }

        y += 212;
        var count = _battleMode ? state.Marks.Length : _war.Orders.TryGetValue(state.Side, out var orders) ? orders.Marks.Length : 0;
        var clear = AddPlate(_right, Loc.GetString("st14-galaxy-tool-clear"), Color.FromHex("#B5533F"), RightX, y, InnerWidth, 52, 15, count == 0);
        clear.OnPressed += _ =>
        {
            if (_battleMode)
                _system.RemoveMark(state.Side, -1);
            else
                _war.RemoveMark(state.Side, -1);
        };
    }

    #endregion
}
