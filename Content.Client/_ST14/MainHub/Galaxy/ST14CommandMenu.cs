using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._ST14.Lobby;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// The command table of a side of the war. One screen with the planet in the middle: on the left an officer tells what to
/// do now and the steps of a turn are listed, on the right is everything the current step needs. The look of the table
/// follows the side it is opened for. It reads the war from the war system and redraws when the war changes
/// </summary>
public sealed partial class ST14CommandMenu : ST14WarScreenBase
{
    public enum Step : byte
    {
        Target,
        Staging,
        Plan,
        Order,
    }

    private const int PresetCount = 6;
    private const float TypeSpeed = 90f;
    private const float WindowY = 124;
    private const float WindowHeight = 846;
    private const float LeftX = 62;
    private const float RightX = 1462;
    private const float InnerWidth = 396;
    private const float BodyTop = 184;
    private const float NextY = 896;

    private static readonly Vector2 WindowSize = new(440, WindowHeight);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Func<ST14GalaxyWarClientSystem?> _system;
    private readonly ST14GalaxyTextures _textures;

    private readonly ST14PlanetGlobe _globe;
    private readonly PanelContainer _tint;
    private readonly PanelContainer _leftWindow;
    private readonly PanelContainer _rightWindow;
    private readonly ST14SlideIn _leftSlide;
    private readonly ST14SlideIn _rightSlide;
    private readonly ST14CanvasLayout _left;
    private readonly ST14CanvasLayout _right;
    private readonly ST14CanvasLayout _modal;
    private readonly ST14CanvasLayout _header;
    private readonly ST14HqBanner _banner;
    private readonly Label _hint;

    private ST14GalaxyMapView? _galaxy;
    private Label? _speechLabel;
    private ST14HubButton? _nextButton;

    private ST14GalaxyPlanetPrototype? _planet;
    private ST14PlanetWar? _war;
    private Step _step = Step.Target;
    private ST14GalaxyFaction _side = ST14GalaxyFaction.Federation;
    private int? _selected;
    private bool _planOnGalaxy;
    private ST14WarMarkKind? _tool;
    private bool _eraser;
    private bool _logOpen;
    private bool _dirty;
    private bool _listening;
    private TimeSpan _battleEnds;
    private string _rightKey = string.Empty;
    private string _bannerText = string.Empty;

    private string _speech = string.Empty;
    private float _speechShown;
    private readonly Dictionary<ST14GalaxyFaction, string> _drafts = new();
    private LineEdit? _input;

    private ScrollContainer? _list;
    private string _listKey = string.Empty;
    private readonly Dictionary<int, ST14HqRow> _rows = new();

    /// <summary>
    /// The player left the table
    /// </summary>
    public event Action? OnClosed;

    public ST14CommandMenu(ST14GalaxyTextures textures, Func<ST14GalaxyWarClientSystem?> system) : base("ST14HubGalaxyMap")
    {
        _textures = textures;
        _system = system;

        _globe = new ST14PlanetGlobe(textures)
        {
            Zoom = 0.9f,
            MinZoom = 0.7f,
            MaxZoom = 1.7f,
            AutoSpin = false,
            TargetMarker = false,
            TagCache = ResCache,
            HoverText = HoverCaption,
        };
        _globe.OnCellSelected += CellClicked;
        _globe.OnRightClicked += GoBack;
        _globe.OnMarkPlaced += mark => System?.AddMark(_side, mark);
        _globe.OnMarkErased += (side, index) => System?.RemoveMark(side, index);
        Canvas.AddAt(_globe, Vector2.Zero, new Vector2(480, 96), new Vector2(960, 892), Vector2.Zero);

        // the room of the hive is lit red
        _tint = new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.FromHex("#5A0F00").WithAlpha(0.32f)), Visible = false };
        Canvas.AddFull(_tint);
        _tint.SetPositionFirst();

        // the windows take the clicks but let the right button through to the table
        _leftWindow = new PanelContainer { MouseFilter = MouseFilterMode.Pass };
        _left = new ST14CanvasLayout();
        _leftSlide = Slide(_leftWindow, _left, 40);

        _rightWindow = new PanelContainer { MouseFilter = MouseFilterMode.Pass };
        _right = new ST14CanvasLayout();
        _rightSlide = Slide(_rightWindow, _right, 1440);

        _header = new ST14CanvasLayout();
        Canvas.AddFull(_header);

        _banner = new ST14HqBanner(ResCache);
        Canvas.AddAt(_banner, Vector2.Zero, new Vector2(790, 100), new Vector2(340, 44), Vector2.Zero);

        AddBottomBar();

        _hint = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Regular, 19, ST14HubStyle.Gold, shadow: true);
        _hint.Align = Label.AlignMode.Center;
        _hint.VAlign = Label.VAlignMode.Center;
        Canvas.AddAt(_hint, new Vector2(0.5f, 1), new Vector2(0, -46.5f), new Vector2(1300, 50));

        _modal = new ST14CanvasLayout { Visible = false };
        Canvas.AddFull(_modal);
    }

    /// <summary>
    /// A window of the table and the layer of its contents, which uses the coordinates of the whole screen. Both slide in together
    /// </summary>
    private ST14SlideIn Slide(PanelContainer window, ST14CanvasLayout content, float x)
    {
        var holder = new ST14CanvasLayout();
        holder.AddAt(window, Vector2.Zero, new Vector2(x, WindowY), WindowSize, Vector2.Zero);
        holder.AddFull(content);

        var slide = new ST14SlideIn();
        slide.AddChild(holder);
        Canvas.AddFull(slide);
        return slide;
    }

    #region Opening and closing

    public void Open(ST14GalaxyPlanetPrototype planet, ST14PlanetWar war)
    {
        _planet = planet;
        _war = war;
        _globe.SetPlanet(planet, war);
        _globe.Zoom = 0.9f;
        Visible = true;

        if (System is { } system)
        {
            if (!_listening)
            {
                system.StateReceived += OnState;
                system.OrdersReceived += OnOrders;
                _listening = true;
            }

            system.Request();
        }

        if (State is { } state)
            ReadBattleTime(state);

        var sides = Sides();
        if (sides.Count > 0 && !sides.Contains(_side))
            _side = sides.First();

        _step = FirstStep();
        _selected = OurTurn || Picked || Battle ? Target : null;
        if (Target is { } target)
            _globe.FaceCell(target);

        _globe.Intro(0.12f, 0.9f);
        _leftSlide.Play(new Vector2(-140, 0));
        _rightSlide.Play(new Vector2(140, 0), delay: 0.08f);
        SetLogOpen(false);
        Refresh();
    }

    public void Close()
    {
        if (!Visible)
            return;

        Visible = false;
        SetToolQuiet(null, false);
        if (_listening && System is { } system)
        {
            system.StateReceived -= OnState;
            system.OrdersReceived -= OnOrders;
        }

        _listening = false;
        OnClosed?.Invoke();
    }

    protected override void GoBack()
    {
        // a step back at a time: the log first, then the tool, then the table
        if (_logOpen)
            SetLogOpen(false);
        else if (_tool != null || _eraser)
            SetTool(null, false);
        else
            Close();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIRightClick)
            return;

        GoBack();
        args.Handle();
    }

    private void OnState(ST14GalaxyWarStateEvent ev)
    {
        if (_war != null && ev.Planet == _planet?.ID)
        {
            _war.ImportState(ev.Cells, ev.Turn);
            ReadBattleTime(ev);
        }

        Refresh();
    }

    private void OnOrders(ST14GalaxyOrdersEvent ev)
    {
        Refresh();
    }

    private void ReadBattleTime(ST14GalaxyWarStateEvent state)
    {
        _battleEnds = _timing.RealTime + TimeSpan.FromSeconds(state.BattleSecondsLeft);
    }

    /// <summary>
    /// Asks for a redraw. It happens once per frame at most, and only when the table has a size to draw at
    /// </summary>
    public void Refresh()
    {
        _dirty = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree)
            return;

        if (_dirty && Canvas.CanvasScale > 0)
            Rebuild();

        // the words of the officer come out letter by letter
        if (_speechLabel != null && _speechShown < _speech.Length)
        {
            _speechShown = MathF.Min(_speech.Length, _speechShown + args.DeltaSeconds * TypeSpeed);
            _speechLabel.Text = _speech[..(int) _speechShown];
        }

        UpdateBanner();
    }

    /// <summary>
    /// Draws the table anew from the state of the war
    /// </summary>
    public void Rebuild()
    {
        _dirty = false;
        if (_war == null)
            return;

        var sides = Sides();
        if (sides.Count > 0 && !sides.Contains(_side))
            _side = sides.First();

        var box = ST14HubStyle.Window(ResCache);
        if (_side == ST14GalaxyFaction.Arachnid)
            box.Modulate = Color.FromHex("#E0A088");
        _leftWindow.PanelOverride = box;
        _rightWindow.PanelOverride = box;
        _tint.Visible = _side == ST14GalaxyFaction.Arachnid;

        BuildHeader(_war, sides);
        BuildLeft(_war);
        UpdateGlobe(_war);
        BuildRight(_war);
        BuildLog(_war);
        UpdateBanner();

        _hint.Text = Loc.GetString(_tool != null || _eraser ? "st14-galaxy-hint-drawing"
                : _step == Step.Plan ? "st14-hq-hint-plan"
                : _step == Step.Order ? "st14-hq-hint-order"
                : "st14-hq-hint-map")
            .ToUpperInvariant();

        ST14HubStyle.ScaleLabels(this, Canvas.CanvasScale);
    }

    #endregion

    #region State

    private ST14GalaxyWarClientSystem? System => _system();

    private HashSet<ST14GalaxyFaction> Sides()
    {
        var sides = new HashSet<ST14GalaxyFaction>();
        if (System is not { } system)
            return sides;

        foreach (var (side, orders) in system.Orders)
        {
            if (orders.CanCommand)
                sides.Add(side);
        }

        return sides;
    }

    private ST14GalaxyWarStateEvent? State =>
        _planet != null && System is { } system && system.TryGetState(_planet.ID, out var state) ? state : null;

    private int? Target => State is { Target: >= 0 } state ? state.Target : null;

    private int? Staging => State is { Staging: >= 0 } state ? state.Staging : null;

    private bool Picked => State?.TargetPicked ?? false;

    private bool Battle => State?.Battle ?? false;

    private ST14GalaxyFaction Turn => _war?.Turn ?? ST14GalaxyFaction.Federation;

    private bool OurTurn => _side == Turn;

    /// <summary>
    /// The command this table is for can make the decision of the turn right now
    /// </summary>
    private bool CanAct => OurTurn && !Battle && Sides().Contains(_side);

    private (string Order, int Marks, string[] Members) OrderOf(ST14GalaxyFaction side)
    {
        return System != null && System.Orders.TryGetValue(side, out var orders)
            ? (orders.Order, orders.Marks.Length, orders.Members)
            : (string.Empty, 0, Array.Empty<string>());
    }

    private IEnumerable<(ST14GalaxyFaction Side, int Index, ST14WarMark Mark)> Marks()
    {
        if (System is not { } system)
            yield break;

        foreach (var (side, orders) in system.Orders)
        {
            for (var i = 0; i < orders.Marks.Length; i++)
            {
                yield return (side, i, orders.Marks[i]);
            }
        }
    }

    private static string SideKey(ST14GalaxyFaction side)
    {
        return side == ST14GalaxyFaction.Arachnid ? "bugs" : "federation";
    }

    private string Say(string key, params (string, object)[] args)
    {
        return Loc.GetString($"st14-hq-say-{SideKey(_side)}-{key}", args);
    }

    private static string FactionName(ST14GalaxyFaction faction)
    {
        return Loc.GetString($"st14-galaxy-faction-{faction.ToString().ToLowerInvariant()}");
    }

    private static Color SideColor(ST14GalaxyFaction side)
    {
        return side == ST14GalaxyFaction.Arachnid ? ST14GalaxyColors.Arachnid : ST14GalaxyColors.Federation;
    }

    private static string TerrainName(ST14HexCell cell)
    {
        return Loc.GetString($"st14-galaxy-terrain-{cell.Terrain + 1}");
    }

    /// <summary>
    /// The attacker of the battle over the target is whoever does not hold it
    /// </summary>
    private bool WeAttack(ST14PlanetWar war, int target)
    {
        return ST14PlanetWar.Enemy(war.Cells[target].Faction) == _side;
    }

    private string? HoverCaption(int cell)
    {
        if (_war == null || cell < 0 || cell >= _war.Cells.Length)
            return null;

        var hex = _war.Cells[cell];
        return $"{hex.Code}  ·  {TerrainName(hex)}".ToUpperInvariant();
    }

    private Step FirstStep()
    {
        if (!CanAct)
            return Step.Plan;

        return Picked ? Step.Plan : Step.Target;
    }

    public void ShowStep(Step step)
    {
        SetToolQuiet(null, false);
        _step = step;
        Refresh();
    }

    public void ShowSide(ST14GalaxyFaction side)
    {
        _side = side;
        _step = FirstStep();
        SetToolQuiet(null, false);
        _selected = OurTurn || Picked || Battle ? Target : null;
        Refresh();
    }

    public void SelectCell(int? cell)
    {
        _selected = cell;
        if (cell is { } index)
            _globe.FaceCellSmooth(index);
        Refresh();
    }

    public void SetLogOpen(bool open)
    {
        _logOpen = open;
        Refresh();
    }

    private void SetToolQuiet(ST14WarMarkKind? tool, bool eraser)
    {
        _tool = tool;
        _eraser = eraser;
        _globe.Tool = tool;
        _globe.Eraser = eraser;
        if (_galaxy != null)
        {
            _galaxy.Tool = tool;
            _galaxy.Eraser = eraser;
        }
    }

    public void SetTool(ST14WarMarkKind? tool, bool eraser)
    {
        SetToolQuiet(tool, eraser);
        Refresh();
    }

    public void SetPlanOnGalaxy(bool galaxy)
    {
        _planOnGalaxy = galaxy;
        SetTool(null, false);
    }

    #endregion

    #region Common parts

    private Label AddLabel(ST14CanvasLayout canvas, string text, ST14HubFont font, int size, Color color, float x, float y, float width, float height)
    {
        var label = ST14HubStyle.Label(ResCache, text, font, size, color, shadow: true);
        canvas.AddAt(label, Vector2.Zero, new Vector2(x, y), new Vector2(width, height), Vector2.Zero);
        return label;
    }

    /// <summary>
    /// A label for a few lines of text, they start from the top of its place
    /// </summary>
    private Label AddText(ST14CanvasLayout canvas, string text, ST14HubFont font, int size, Color color, float x, float y, float width, float height)
    {
        var label = AddLabel(canvas, text, font, size, color, x, y, width, height);
        label.VerticalAlignment = VAlignment.Top;
        return label;
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

    #region Header

    private void BuildHeader(ST14PlanetWar war, HashSet<ST14GalaxyFaction> sides)
    {
        _header.RemoveAllChildren();

        var emblem = new ST14HqPortrait { Portrait = ST14HqArt.Tex(ResCache, _side == ST14GalaxyFaction.Arachnid ? "emblem_hive.png" : "emblem_federation.png") };
        _header.AddAt(emblem, Vector2.Zero, new Vector2(40, 16), new Vector2(96, 96), Vector2.Zero);

        AddLabel(_header, Loc.GetString($"st14-hq-title-{SideKey(_side)}").ToUpperInvariant(), ST14HubFont.Black, 40, ST14HubStyle.GoldLight, 148, 18, 620, 58);

        var turnNumber = (State?.Log.Length ?? 0) + 1;
        var path = Loc.GetString("st14-hq-path", ("planet", _planet != null ? Loc.GetString(_planet.Name) : string.Empty), ("turn", turnNumber)).ToUpperInvariant();
        AddLabel(_header, path, ST14HubFont.Regular, 19, ST14HubStyle.Gold, 150, 74, 620, 30);

        var score = new ST14HqScore(ResCache)
        {
            Federation = war.CountOf(ST14GalaxyFaction.Federation),
            Hive = war.Cells.Length - war.CountOf(ST14GalaxyFaction.Federation),
            FederationName = FactionName(ST14GalaxyFaction.Federation).ToUpperInvariant(),
            HiveName = FactionName(ST14GalaxyFaction.Arachnid).ToUpperInvariant(),
        };
        _header.AddAt(score, Vector2.Zero, new Vector2(780, 20), new Vector2(620, 42), Vector2.Zero);

        var regions = war.RegionCenters.Length;
        var taken = Enumerable.Range(0, regions).Count(r => war.Cells.Where(c => c.Region == r).All(c => c.Faction == ST14GalaxyFaction.Federation));
        var stats = AddLabel(_header, Loc.GetString("st14-hq-stats", ("taken", taken), ("regions", regions), ("bugs", war.BugsKilled), ("troopers", war.TroopersKilled)).ToUpperInvariant(),
            ST14HubFont.Regular, 14, ST14HubStyle.Gold, 780, 64, 620, 26);
        stats.Align = Label.AlignMode.Center;

        // the command to act for: a role opens its own side, someone in both, like an admin, switches between them
        var x = 1440f;
        foreach (var side in ST14GalaxyWarRules.Sides)
        {
            var target = side;
            var name = Loc.GetString($"st14-galaxy-side-{SideKey(side)}");
            var tint = side == _side ? (side == ST14GalaxyFaction.Arachnid ? Color.FromHex("#C0503A") : ST14HubStyle.Blue) : ST14HqArt.PlateOff;
            var pick = AddPlate(_header, name, tint, x, 26, 216, 62, 18, !sides.Contains(side));
            pick.PressSound = ST14HubSound.Tab;
            pick.OnPressed += _ => ShowSide(target);
            x += 224;
        }
    }

    private void UpdateBanner()
    {
        if (_war == null)
            return;

        var state = State;
        var battle = state?.Battle ?? false;
        string text;
        if (battle)
        {
            var left = _battleEnds - _timing.RealTime;
            if (left < TimeSpan.Zero)
                left = TimeSpan.Zero;

            text = Loc.GetString("st14-hq-banner-battle", ("time", $"{(int) left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}"));
        }
        else
        {
            text = Loc.GetString(OurTurn ? "st14-hq-banner-ours" : "st14-hq-banner-enemy", ("side", FactionName(Turn)));
        }

        _banner.Battle = battle;
        _banner.Lit = !battle && OurTurn;
        if (text == _bannerText)
            return;

        _bannerText = text;
        _banner.Text = text.ToUpperInvariant();
    }

    #endregion

    #region Left: the report and the steps

    private void BuildLeft(ST14PlanetWar war)
    {
        _left.RemoveAllChildren();
        _speechLabel = null;

        AddTitle(_left, Loc.GetString("st14-hq-report"), 40);

        var portrait = new ST14HqPortrait { Portrait = ST14HqArt.Tex(ResCache, _side == ST14GalaxyFaction.Arachnid ? "portrait_brainbug.png" : "portrait_radio.png") };
        _left.AddAt(portrait, Vector2.Zero, new Vector2(LeftX, BodyTop - 4), new Vector2(128, 128), Vector2.Zero);

        AddText(_left, Loc.GetString($"st14-hq-speaker-{SideKey(_side)}").ToUpperInvariant(), ST14HubFont.SemiBold, 14, SideColor(_side), LeftX + 138, BodyTop - 4, 258, 22);

        var speech = Wrap(Speech(war), 26);
        if (speech != _speech)
        {
            _speech = speech;
            _speechShown = 0;
        }

        _speechLabel = AddText(_left, _speech[..(int) MathF.Min(_speechShown, _speech.Length)], ST14HubFont.Regular, 14, Color.White, LeftX + 138, BodyTop + 20, 258, 126);

        var y = BodyTop + 150;
        AddStep(1, Step.Target, Loc.GetString("st14-hq-step-target"), TargetValue(war), TargetStatus(), ref y, true);
        AddStep(2, Step.Staging, Loc.GetString("st14-hq-step-staging"), StagingValue(war), StagingStatus(), ref y, true);

        var plan = OrderOf(_side);
        AddStep(3, Step.Plan, Loc.GetString("st14-hq-step-plan"),
            plan.Marks > 0 ? Loc.GetString("st14-hq-plan-value", ("count", plan.Marks)) : Loc.GetString("st14-hq-plan-empty"),
            plan.Marks > 0 ? ST14HqStepStatus.Done : ST14HqStepStatus.Todo, ref y, true);

        AddStep(4, Step.Order, Loc.GetString($"st14-hq-step-order-{SideKey(_side)}"),
            plan.Order.Length > 0 ? plan.Order : Loc.GetString("st14-hq-order-empty"),
            plan.Order.Length > 0 ? ST14HqStepStatus.Done : ST14HqStepStatus.Todo, ref y, false);

        // the latest battles, the whole log is one press away
        AddText(_left, Loc.GetString("st14-hq-last-battles").ToUpperInvariant(), ST14HubFont.Medium, 14, ST14HubStyle.Gold, LeftX, y, InnerWidth, 22);
        y += 24;

        var log = State?.Log ?? Array.Empty<ST14WarLogEntry>();
        if (log.Length == 0)
            AddText(_left, Loc.GetString("st14-hq-log-empty").ToUpperInvariant(), ST14HubFont.Regular, 13, ST14HqArt.Muted, LeftX, y + 2, InnerWidth, 22);

        foreach (var entry in log.Reverse().Take(3))
        {
            var row = new ST14HqRow(ResCache)
            {
                Text = LogShort(war, entry),
                TextColor = LogColor(entry),
                Disabled = true,
            };
            _left.AddAt(row, Vector2.Zero, new Vector2(LeftX - 6, y), new Vector2(InnerWidth + 12, 36), Vector2.Zero);
            y += 38;
        }

        var members = plan.Members.Length > 0 ? string.Join(", ", plan.Members.Take(3)) : Loc.GetString("st14-hq-online-nobody");
        AddText(_left, Loc.GetString("st14-hq-online", ("names", members)).ToUpperInvariant(), ST14HubFont.Regular, 13, ST14HqArt.Muted, LeftX, NextY - 24, InnerWidth, 22);

        var journal = AddPlate(_left, Loc.GetString("st14-hq-log-open", ("count", log.Length)), null, LeftX, NextY, InnerWidth, 58, 16);
        journal.OnPressed += _ => SetLogOpen(!_logOpen);
    }

    private void AddTitle(ST14CanvasLayout canvas, string text, float windowX)
    {
        // engraved into the brass of the window
        var title = ST14HubStyle.Label(ResCache, text.ToUpperInvariant(), ST14HubFont.Bold, 18, ST14HqArt.Dark);
        canvas.AddAt(title, Vector2.Zero, new Vector2(windowX + 46, WindowY + 6), new Vector2(340, 36), Vector2.Zero);
    }

    private void AddStep(int number, Step step, string title, string value, ST14HqStepStatus status, ref float y, bool chain)
    {
        var button = new ST14HqStep(ResCache)
        {
            Number = number,
            Title = title.ToUpperInvariant(),
            Value = value.ToUpperInvariant(),
            Status = status,
            Active = _step == step,
            ChainBelow = chain,
            ChainGap = 8,
            Accent = ST14HubStyle.GoldLight,
        };
        button.OnPressed += _ => ShowStep(step);
        _left.AddAt(button, Vector2.Zero, new Vector2(LeftX, y), new Vector2(InnerWidth, 88), Vector2.Zero);
        y += 96;
    }

    private ST14HqStepStatus TargetStatus()
    {
        if (Battle || !OurTurn)
            return ST14HqStepStatus.Locked;

        return Picked ? ST14HqStepStatus.Done : ST14HqStepStatus.Todo;
    }

    private string TargetValue(ST14PlanetWar war)
    {
        if (Target is not { } target)
            return Loc.GetString("st14-hq-target-none");

        var code = war.Cells[target].Code;
        if (Battle)
            return Loc.GetString("st14-hq-target-battle", ("code", code));

        if (!OurTurn)
            return Picked ? Loc.GetString("st14-hq-target-enemy", ("code", code)) : Loc.GetString("st14-hq-target-enemy-wait");

        return Picked
            ? $"{code}  ·  {TerrainName(war.Cells[target])}"
            : Loc.GetString("st14-hq-target-default", ("code", code));
    }

    private ST14HqStepStatus StagingStatus()
    {
        if (Battle || !OurTurn)
            return ST14HqStepStatus.Locked;

        return Picked ? ST14HqStepStatus.Done : ST14HqStepStatus.Todo;
    }

    private string StagingValue(ST14PlanetWar war)
    {
        if (Target is not { } target || !OurTurn && !Picked && !Battle)
            return Loc.GetString("st14-hq-staging-none");

        if (Staging is { } staging)
            return Loc.GetString(WeAttack(war, target) ? "st14-hq-staging-ours" : "st14-hq-staging-enemy", ("code", war.Cells[staging].Code));

        return Loc.GetString(WeAttack(war, target) ? "st14-hq-staging-landing" : "st14-hq-staging-enemy-landing");
    }

    /// <summary>
    /// What the officer says: what to do next, or what is going on when there is nothing to decide
    /// </summary>
    private string Speech(ST14PlanetWar war)
    {
        if (_step == Step.Plan && _tool is { } tool)
            return Loc.GetString($"st14-hq-tool-{tool.ToString().ToLowerInvariant()}");

        if (_step == Step.Plan && _eraser)
            return Loc.GetString("st14-hq-tool-eraser");

        var code = Target is { } target ? war.Cells[target].Code : "-";
        if (_step == Step.Plan)
            return Say("plan");

        if (_step == Step.Order)
            return OrderOf(_side).Order.Length > 0 && Picked ? Say("ready") : Say("order");

        if (Battle)
            return Say("battle", ("code", code));

        if (!OurTurn)
            return Picked ? Say("enemy-target", ("code", code)) : Say("enemy-wait");

        if (_step == Step.Staging)
        {
            if (Target is not { } battle)
                return Say("target");

            return war.StagingOptions(battle).Any() ? Say("staging") : Say(WeAttack(war, battle) ? "landing" : "hold-landing");
        }

        // with no Federation ground left the bugs only choose what to hold
        if (_side == ST14GalaxyFaction.Arachnid && war.CountOf(ST14GalaxyFaction.Federation) == 0)
            return Picked ? Say("target-done", ("code", code)) : Say("hold");

        return Picked ? Say("target-done", ("code", code)) : Say("target");
    }

    #endregion

    #region The planet

    private void UpdateGlobe(ST14PlanetWar war)
    {
        // an enemy decision that was not made yet stays hidden, the fallback is no information
        var showBattle = Target != null && (OurTurn || Picked || Battle);
        _globe.TargetCell = showBattle ? Target : null;
        _globe.StagingCell = showBattle && (Picked || Battle) ? Staging : null;
        _globe.StagingColor = Target is { } attacked ? SideColor(ST14PlanetWar.Enemy(war.Cells[attacked].Faction)) : ST14HubStyle.GoldLight;
        _globe.SelectedCell = _step is Step.Target or Step.Staging ? _selected : null;
        // a member of both commands sees one plan at a time, the one of the room they are in
        _globe.Marks = Marks().Where(m => m.Side == _side && m.Mark.Surface).ToList();
        _globe.ToolSide = _side;
        _globe.Tool = _tool;
        _globe.Eraser = _eraser;

        _globe.Focus = null;
        if (CanAct && _step == Step.Target)
        {
            _globe.Focus = Enumerable.Range(0, war.Cells.Length).Where(i => war.Cells[i].Front).ToHashSet();
            _globe.FocusColor = ST14HubStyle.GoldLight;
        }
        else if (CanAct && _step == Step.Staging && Target is { } battle)
        {
            _globe.Focus = war.StagingOptions(battle).ToHashSet();
            _globe.FocusColor = SideColor(_side);
        }

        _globe.Tags.Clear();
        _globe.TargetIcon = null;
        if (showBattle && Target is { } target)
        {
            var code = war.Cells[target].Code;
            var landing = Staging == null;
            if (!Picked && !Battle)
            {
                // only what the round would fall back on, no decision yet
                _globe.Tags.Add((target, Loc.GetString("st14-hq-tag-default", ("code", code)).ToUpperInvariant(), ST14HqArt.Muted));
            }
            else
            {
                _globe.TargetIcon = ST14HqArt.Tex(ResCache, landing ? "icon_landing.png" : "icon_objective.png");
                _globe.TargetIconColor = landing ? Color.White : ST14HqArt.Dark;
                _globe.Tags.Add((target, Loc.GetString(landing ? "st14-hq-tag-landing" : "st14-hq-tag-target", ("code", code)).ToUpperInvariant(), ST14HubStyle.GoldLight));
                if (Staging is { } staging)
                    _globe.Tags.Add((staging, Loc.GetString("st14-hq-tag-staging", ("code", war.Cells[staging].Code)).ToUpperInvariant(), Color.White));
            }
        }

        var onGalaxy = _step == Step.Plan && _planOnGalaxy;
        _globe.Visible = !onGalaxy;
        if (!onGalaxy && _galaxy == null)
            return;

        if (_galaxy == null)
        {
            _galaxy = new ST14GalaxyMapView(_prototypes, ResCache, _textures);
            _galaxy.OnMarkPlaced += mark => System?.AddMark(_side, mark);
            _galaxy.OnMarkErased += (side, index) => System?.RemoveMark(side, index);
            _galaxy.OnToolCancelled += () => SetTool(null, false);
            _galaxy.OnBackClicked += GoBack;
            Canvas.AddAt(_galaxy, Vector2.Zero, new Vector2(490, 150), new Vector2(940, 810), Vector2.Zero);
            _galaxy.SetPositionInParent(_globe.GetPositionInParent() + 1);
        }

        _galaxy.Visible = onGalaxy;
        _galaxy.Marks = Marks().Where(m => m.Side == _side && !m.Mark.Surface).ToList();
        _galaxy.ToolSide = _side;
        _galaxy.Tool = _tool;
        _galaxy.Eraser = _eraser;
    }

    private void CellClicked(int? cell)
    {
        if (cell is not { } index || _war == null)
            return;

        // choosing where to gather is a single click, the choice of the target is confirmed with its button
        if (_step == Step.Staging && CanAct && Target is { } target && _war.StagingOptions(target).Contains(index) && _planet != null)
            System?.Staging(_planet.ID, index);

        if (_step is Step.Target or Step.Staging)
        {
            _selected = index;
            Refresh();
        }
    }

    #endregion

    #region Right: the current step

    private void BuildRight(ST14PlanetWar war)
    {
        var order = OrderOf(_side);
        var picked = war.Cells[_selected ?? 0];
        var marks = string.Join(',', Marks().Where(m => m.Side == _side).Select(m => (int) m.Mark.Kind));
        var key = $"{_step}|{_side}|{_selected}|{Target}|{Staging}|{Picked}|{Battle}|{Turn}|{_tool}|{_eraser}|{_planOnGalaxy}|{order.Order}|{marks}|{picked.Faction}|{picked.Initialized}";

        // a redraw must not take the text from under the fingers of whoever is typing an order
        if (key == _rightKey || _input?.HasKeyboardFocus() == true)
            return;

        _rightKey = key;
        _list?.Orphan();
        _right.RemoveAllChildren();
        _input = null;
        _nextButton = null;

        AddTitle(_right, Loc.GetString($"st14-hq-panel-{_step.ToString().ToLowerInvariant()}", ("number", (int) _step + 1)), 1440);

        switch (_step)
        {
            case Step.Target:
                BuildTargetStep(war);
                break;
            case Step.Staging:
                BuildStagingStep(war);
                break;
            case Step.Plan:
                BuildPlanStep();
                break;
            case Step.Order:
                BuildOrderStep();
                break;
        }
    }

    private void AddNext(Step next, Color? tint)
    {
        var name = Loc.GetString(next == Step.Order ? $"st14-hq-step-order-{SideKey(_side)}" : $"st14-hq-step-{next.ToString().ToLowerInvariant()}");
        _nextButton = AddPlate(_right, Loc.GetString("st14-hq-next", ("step", name)), tint, RightX, NextY, InnerWidth, 58, 16);
        _nextButton.OnPressed += _ => ShowStep(next);
    }

    private void AddFact(string key, string value, Color color, ref float y)
    {
        AddLabel(_right, Loc.GetString(key).ToUpperInvariant(), ST14HubFont.Regular, 13, ST14HqArt.Muted, RightX, y, 170, 24);
        var shown = AddLabel(_right, value.ToUpperInvariant(), ST14HubFont.Medium, 14, color, RightX + 120, y, InnerWidth - 120, 24);
        shown.Align = Label.AlignMode.Right;
        y += 25;
    }

    /// <summary>
    /// What is known about a sector, returns the height under it
    /// </summary>
    private float AddSectorCard(ST14PlanetWar war, int index, float y)
    {
        var cell = war.Cells[index];
        AddLabel(_right, Loc.GetString("st14-galaxy-cell-name", ("code", cell.Code)).ToUpperInvariant(), ST14HubFont.Black, 26, ST14HubStyle.GoldLight, RightX, y, InnerWidth, 40);
        y += 44;

        var kills = war.RegionKills(index);
        AddFact("st14-hq-fact-control", FactionName(cell.Faction), SideColor(cell.Faction), ref y);
        AddFact("st14-hq-fact-terrain", TerrainName(cell), Color.White, ref y);
        AddFact("st14-hq-fact-region", war.Cells[war.RegionCenters[cell.Region]].Code, Color.White, ref y);
        AddFact("st14-hq-fact-fights", Loc.GetString(cell.Initialized ? "st14-hq-fought" : "st14-hq-not-fought"), cell.Initialized ? ST14HubStyle.GoldLight : ST14HqArt.Muted, ref y);
        AddFact("st14-hq-fact-losses", Loc.GetString("st14-hq-losses", ("bugs", kills.Bugs), ("troopers", kills.Troopers)), Color.White, ref y);

        return y + 6;
    }

    private void BuildTargetStep(ST14PlanetWar war)
    {
        var y = BodyTop;
        var target = Target;
        if (_selected is { } index && index >= 0 && index < war.Cells.Length)
        {
            y = AddSectorCard(war, index, y);
            var cell = war.Cells[index];
            var planetId = _planet!.ID;

            if (target == index && Picked && !Battle)
            {
                // the decision is made: it gets its stamp
                _right.AddAt(new ST14HqStamp(ResCache, Loc.GetString(OurTurn ? "st14-hq-stamp-target" : "st14-hq-stamp-enemy")), Vector2.Zero, new Vector2(RightX + 48, y + 4), new Vector2(300, 54), Vector2.Zero);
            }
            else if (Battle)
            {
                AddPlate(_right, Loc.GetString("st14-hq-action-battle"), null, RightX, y, InnerWidth, 62, 17, true);
            }
            else if (!OurTurn)
            {
                AddPlate(_right, Loc.GetString("st14-hq-action-enemy"), null, RightX, y, InnerWidth, 62, 17, true);
            }
            else if (!cell.Front)
            {
                AddPlate(_right, Loc.GetString("st14-hq-action-closed"), null, RightX, y, InnerWidth, 62, 17, true);
            }
            else
            {
                var confirm = AddPlate(_right, Loc.GetString("st14-hq-action-pick"), ST14HubStyle.Orange, RightX, y, InnerWidth, 62, 18, !CanAct);
                confirm.OnPressed += _ =>
                {
                    System?.Order(planetId, index);
                    ShowStep(Step.Staging);
                };
            }

            y += 72;
        }
        else
        {
            AddText(_right, Wrap(Loc.GetString("st14-hq-pick-hint").ToUpperInvariant(), 30), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 60);
            y += 70;
        }

        // while the battle is on there is nothing to pick from, only what comes after it
        if (Battle)
        {
            AddText(_right, Wrap(Loc.GetString("st14-hq-battle-next", ("side", FactionName(ST14PlanetWar.Enemy(Turn)))).ToUpperInvariant(), 32), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 90);
            AddNext(Step.Plan, null);
            return;
        }

        var options = Enumerable.Range(0, war.Cells.Length).Where(i => war.Cells[i].Front)
            .OrderByDescending(i => i == target)
            .ThenBy(i => !war.Cells[i].Initialized)
            .ThenBy(i => war.Cells[i].Code)
            .ToList();

        AddLabel(_right, Loc.GetString(OurTurn ? "st14-hq-options" : "st14-hq-options-enemy", ("count", options.Count)).ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 24);
        y += 28;

        var listKey = "targets" + string.Join(',', options);
        if (listKey != _listKey || _list == null)
        {
            _listKey = listKey;
            _rows.Clear();
            var scale = Canvas.CanvasScale;
            var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
            foreach (var option in options)
            {
                var pick = option;
                var row = new ST14HqRow(ResCache)
                {
                    SetHeight = 44 * scale,
                    HorizontalExpand = true,
                    Text = war.Cells[option].Code,
                    Note = TerrainName(war.Cells[option]).ToUpperInvariant(),
                };
                row.OnPressed += _ => SelectCell(pick);
                _rows[option] = row;
                box.AddChild(row);
            }

            _list = new ScrollContainer { HScrollEnabled = false };
            _list.AddChild(box);
        }

        foreach (var (option, row) in _rows)
        {
            row.Selected = option == _selected;
            row.Icon = option == target && (Picked || OurTurn) ? ST14HqArt.Tex(ResCache, "icon_objective.png") : null;
        }

        _right.AddAt(_list, Vector2.Zero, new Vector2(RightX - 6, y), new Vector2(InnerWidth + 12, NextY - y - 10), Vector2.Zero);
        AddNext(CanAct ? Step.Staging : Step.Plan, Picked && OurTurn ? ST14HubStyle.Orange : null);
    }

    private void BuildStagingStep(ST14PlanetWar war)
    {
        var y = BodyTop;
        if (Target is not { } target)
        {
            AddText(_right, Wrap(Loc.GetString("st14-hq-target-none").ToUpperInvariant(), 30), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 60);
            AddNext(Step.Plan, null);
            return;
        }

        var options = war.StagingOptions(target).ToList();
        if (!WeAttack(war, target) || !OurTurn && !Picked && !Battle)
        {
            AddText(_right, Wrap(Loc.GetString("st14-hq-staging-defend").ToUpperInvariant(), 32), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 90);
            y += 90;
            if (Staging is { } theirs && (Picked || Battle))
                AddSectorCard(war, theirs, y);

            AddNext(Step.Plan, null);
            return;
        }

        if (options.Count == 0)
        {
            var art = new ST14HqPortrait { Portrait = ST14HqArt.Tex(ResCache, "icon_landing.png") };
            _right.AddAt(art, Vector2.Zero, new Vector2(RightX + 148, y), new Vector2(100, 108), Vector2.Zero);
            y += 118;

            var title = AddLabel(_right, Loc.GetString("st14-hq-landing-title").ToUpperInvariant(), ST14HubFont.Black, 20, ST14HubStyle.GoldLight, RightX, y, InnerWidth, 34);
            title.Align = Label.AlignMode.Center;
            y += 40;

            var text = AddText(_right, Wrap(Loc.GetString("st14-hq-landing-text"), 40), ST14HubFont.Regular, 15, Color.White, RightX, y, InnerWidth, 90);
            text.Align = Label.AlignMode.Center;
            y += 100;

            AddLabel(_right, Loc.GetString("st14-hq-staging-target").ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 24);
            AddSectorCard(war, target, y + 26);

            AddNext(Step.Plan, ST14HubStyle.Orange);
            return;
        }

        AddText(_right, Wrap(Loc.GetString("st14-hq-staging-help").ToUpperInvariant(), 32), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 56);
        y += 58;

        foreach (var option in options)
        {
            var pick = option;
            var row = new ST14HqRow(ResCache)
            {
                Text = war.Cells[option].Code,
                Note = TerrainName(war.Cells[option]).ToUpperInvariant(),
                Selected = option == Staging,
                Disabled = !CanAct,
            };
            row.OnPressed += _ =>
            {
                if (_planet != null)
                    System?.Staging(_planet.ID, pick);
                SelectCell(pick);
            };
            _right.AddAt(row, Vector2.Zero, new Vector2(RightX - 6, y), new Vector2(InnerWidth + 12, 44), Vector2.Zero);
            y += 47;
        }

        y += 8;
        AddLabel(_right, Loc.GetString("st14-hq-staging-target").ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 24);
        AddSectorCard(war, target, y + 26);

        AddNext(Step.Plan, ST14HubStyle.Orange);
    }

    private void BuildPlanStep()
    {
        var y = BodyTop;
        var planet = _planet != null ? Loc.GetString(_planet.Name) : string.Empty;
        AddPlate(_right, planet, _planOnGalaxy ? ST14HqArt.PlateOff : ST14HubStyle.Blue, RightX, y, 194, 52, 15).OnPressed += _ => SetPlanOnGalaxy(false);
        AddPlate(_right, Loc.GetString("st14-hq-plan-galaxy"), _planOnGalaxy ? ST14HubStyle.Blue : ST14HqArt.PlateOff, RightX + 202, y, 194, 52, 15).OnPressed += _ => SetPlanOnGalaxy(true);
        y += 66;

        var tools = new (ST14WarMarkKind? Kind, string Key, string Icon)[]
        {
            (ST14WarMarkKind.Attack, "attack", "icon_attack.png"),
            (ST14WarMarkKind.Defend, "defend", "icon_defend.png"),
            (ST14WarMarkKind.Rally, "rally", "icon_rally.png"),
            (ST14WarMarkKind.Danger, "danger", "icon_danger.png"),
            (ST14WarMarkKind.Objective, "objective", "icon_objective.png"),
            (null, "eraser", "icon_erase.png"),
        };

        for (var i = 0; i < tools.Length; i++)
        {
            var (kind, key, icon) = tools[i];
            var active = kind != null ? _tool == kind : _eraser;
            var tile = new ST14HqTile(ResCache, Loc.GetString($"st14-galaxy-tile-{key}").ToUpperInvariant(), ST14HqArt.Tex(ResCache, icon), active);
            tile.OnPressed += _ =>
            {
                if (kind != null)
                    SetTool(active ? null : kind, false);
                else
                    SetTool(null, !active);
            };
            _right.AddAt(tile, Vector2.Zero, new Vector2(RightX + i % 3 * 134, y + i / 3 * 126), new Vector2(128, 118), Vector2.Zero);
        }

        y += 258;

        var marks = Marks().Where(m => m.Side == _side).ToList();
        AddLabel(_right, Loc.GetString("st14-hq-plan-count", ("count", marks.Count), ("max", ST14GalaxyOrdersEvent.MaxMarks)).ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 26);
        y += 28;

        // what stands on the map, by the kind of the sign
        var kinds = tools.Where(t => t.Kind != null && marks.Any(m => m.Mark.Kind == t.Kind)).ToList();
        foreach (var (kind, key, icon) in kinds)
        {
            var row = new ST14HqRow(ResCache)
            {
                Text = Loc.GetString($"st14-galaxy-tile-{key}").ToUpperInvariant(),
                Note = marks.Count(m => m.Mark.Kind == kind).ToString(),
                Icon = ST14HqArt.Tex(ResCache, icon),
                Disabled = true,
            };
            _right.AddAt(row, Vector2.Zero, new Vector2(RightX - 6, y), new Vector2(InnerWidth + 12, 38), Vector2.Zero);
            y += 40;
        }

        if (kinds.Count == 0)
            AddText(_right, Wrap(Loc.GetString(_tool == null && !_eraser ? "st14-hq-plan-help" : "st14-hq-plan-help-tool").ToUpperInvariant(), 34), ST14HubFont.Regular, 14, ST14HqArt.Muted, RightX, y, InnerWidth, 90);

        var clear = AddPlate(_right, Loc.GetString("st14-galaxy-tool-clear"), Color.FromHex("#C0503A"), RightX, NextY - 60, InnerWidth, 52, 15, marks.Count == 0);
        clear.OnPressed += _ => System?.RemoveMark(_side, -1);

        AddNext(Step.Order, marks.Count > 0 ? ST14HubStyle.Orange : null);
    }

    private void BuildOrderStep()
    {
        var side = _side;
        var y = BodyTop;
        var current = OrderOf(side).Order;

        AddLabel(_right, Loc.GetString("st14-hq-order-current").ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 24);
        y += 26;
        AddText(_right, Wrap((current.Length > 0 ? current : Loc.GetString("st14-hq-order-empty")).ToUpperInvariant(), 30), ST14HubFont.Bold, 17,
            current.Length > 0 ? ST14HubStyle.GoldLight : ST14HqArt.Muted, RightX, y, InnerWidth, 76);
        y += 74;

        var input = new LineEdit
        {
            Text = _drafts.GetValueOrDefault(side, string.Empty),
            PlaceHolder = Loc.GetString("st14-galaxy-order-placeholder"),
            StyleBoxOverride = ST14HubStyle.Flat(new Color(0.01f, 0.02f, 0.035f, 0.95f), ST14HubStyle.Gold.WithAlpha(0.7f), 1.2f),
        };
        _input = input;
        input.OnTextChanged += args => _drafts[side] = args.Text;
        input.OnTextEntered += args => Send(side, args.Text);
        _right.AddAt(input, Vector2.Zero, new Vector2(RightX, y), new Vector2(InnerWidth, 46), Vector2.Zero);
        y += 54;

        AddPlate(_right, Loc.GetString("st14-galaxy-order-send"), ST14HubStyle.Orange, RightX, y, 244, 58, 16).OnPressed += _ => Send(side, input.Text);
        AddPlate(_right, Loc.GetString("st14-hq-order-cancel"), ST14HqArt.PlateOff, RightX + 252, y, 144, 58, 14, current.Length == 0).OnPressed += _ => Send(side, string.Empty);
        y += 70;

        AddLabel(_right, Loc.GetString("st14-hq-order-presets").ToUpperInvariant(), ST14HubFont.Medium, 15, ST14HubStyle.Gold, RightX, y, InnerWidth, 24);
        y += 28;

        for (var i = 0; i < PresetCount; i++)
        {
            var text = Loc.GetString($"st14-hq-preset-{SideKey(side)}-{i + 1}");
            var row = new ST14HqRow(ResCache) { Text = text.ToUpperInvariant() };
            row.OnPressed += _ =>
            {
                _drafts[side] = text;
                input.Text = text;
            };
            _right.AddAt(row, Vector2.Zero, new Vector2(RightX - 6, y), new Vector2(InnerWidth + 12, 44), Vector2.Zero);
            y += 46;
        }

        var done = AddPlate(_right, Loc.GetString("st14-hq-done"), current.Length > 0 ? ST14HubStyle.Orange : ST14HqArt.PlateOff, RightX, NextY, InnerWidth, 58, 16);
        done.OnPressed += _ => Close();
    }

    private void Send(ST14GalaxyFaction side, string text)
    {
        System?.SetOrder(side, text);
        _drafts.Remove(side);
        _input?.ReleaseKeyboardFocus();
        _rightKey = string.Empty;
    }

    #endregion

    #region The log

    private void BuildLog(ST14PlanetWar war)
    {
        _modal.Visible = _logOpen;
        if (!_logOpen)
            return;

        _modal.RemoveAllChildren();

        var dim = new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black.WithAlpha(0.55f)), MouseFilter = MouseFilterMode.Pass };
        _modal.AddFull(dim);

        var box = ST14HubStyle.Window(ResCache);
        var window = new PanelContainer { PanelOverride = box, MouseFilter = MouseFilterMode.Pass };
        _modal.AddAt(window, Vector2.Zero, new Vector2(490, 152), new Vector2(940, 778), Vector2.Zero);

        var log = State?.Log ?? Array.Empty<ST14WarLogEntry>();
        var title = AddLabel(_modal, Loc.GetString("st14-hq-log-title", ("count", log.Length)).ToUpperInvariant(), ST14HubFont.Bold, 18, ST14HubStyle.GoldLight, 536, 158, 700, 36);
        title.VAlign = Label.VAlignMode.Center;

        if (log.Length == 0)
        {
            AddLabel(_modal, Loc.GetString("st14-hq-log-empty").ToUpperInvariant(), ST14HubFont.Regular, 17, ST14HqArt.Muted, 530, 222, 860, 30);
        }
        else
        {
            var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
            var scale = Canvas.CanvasScale;
            foreach (var entry in log.Reverse())
            {
                list.AddChild(new ST14HqRow(ResCache)
                {
                    Text = LogLine(war, entry),
                    TextColor = LogColor(entry),
                    SetHeight = 44 * scale,
                    HorizontalExpand = true,
                    Disabled = true,
                });
            }

            var scroll = new ScrollContainer { HScrollEnabled = false };
            scroll.AddChild(list);
            _modal.AddAt(scroll, Vector2.Zero, new Vector2(516, 212), new Vector2(888, 618), Vector2.Zero);
        }

        AddPlate(_modal, Loc.GetString("st14-hq-log-close"), ST14HubStyle.Orange, 760, 846, 400, 58, 16).OnPressed += _ => SetLogOpen(false);
    }

    /// <summary>
    /// A log line short enough for the side of the table
    /// </summary>
    private static string LogShort(ST14PlanetWar war, ST14WarLogEntry entry)
    {
        var code = entry.Cell >= 0 && entry.Cell < war.Cells.Length ? war.Cells[entry.Cell].Code : "?";
        var key = entry.Winner == null ? "st14-hq-log-short-time"
            : entry.Winner == entry.Attacker ? "st14-hq-log-short-taken"
            : "st14-hq-log-short-held";

        return Loc.GetString(key, ("code", code), ("winner", entry.Winner is { } winner ? FactionName(winner) : string.Empty)).ToUpperInvariant();
    }

    private static Color LogColor(ST14WarLogEntry entry)
    {
        return entry.Winner is { } winner ? SideColor(winner) : ST14HqArt.Muted;
    }

    private static string LogLine(ST14PlanetWar war, ST14WarLogEntry entry)
    {
        var code = entry.Cell >= 0 && entry.Cell < war.Cells.Length ? war.Cells[entry.Cell].Code : "?";
        var key = entry.Winner == null ? "st14-hq-log-time"
            : entry.Winner == entry.Attacker ? "st14-hq-log-taken"
            : "st14-hq-log-held";

        return Loc.GetString(key,
                ("round", entry.Round),
                ("code", code),
                ("attacker", FactionName(entry.Attacker)),
                ("winner", entry.Winner is { } winner ? FactionName(winner) : string.Empty))
            .ToUpperInvariant();
    }

    #endregion
}
