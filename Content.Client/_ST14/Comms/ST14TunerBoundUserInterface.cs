using System.Numerics;
using Content.Shared._ST14.Comms;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Comms;

/// <summary>
/// The tuner of the field radio. On a comms tower the signaller searches the hidden frequency of the Rodger Young by
/// the strength of the signal and holds the dial on it until it is caught. On a radio pack the signaller sets the pack
/// to the frequency a tower holds, switches it, picks the power of the transmitter and keeps frequencies in memory
/// </summary>
[UsedImplicitly]
public sealed class ST14TunerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(0.05);

    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();

    private ST14TunerWindow? _window;
    private TimeSpan _nextSend;
    private int? _pending;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ST14TunerWindow>();
        _window.OnDial += dial => _pending = dial;
        _window.OnFrame += SendPending;
        _window.OnPower += on => SendMessage(new ST14RadioPackPowerMessage(on));
        _window.OnRange += power => SendMessage(new ST14RadioPackRangeMessage(power));
        _window.OnPreset += (slot, save) =>
        {
            // a dial turned just before must reach the pack before it is saved
            FlushPending();
            SendMessage(new ST14RadioPackPresetMessage(slot, save));
        };
    }

    private void FlushPending()
    {
        if (_pending is not { } dial)
            return;

        _pending = null;
        SendMessage(new ST14TunerDialMessage(dial));
    }

    private void SendPending()
    {
        if (_pending == null || _timing.RealTime < _nextSend)
            return;

        _nextSend = _timing.RealTime + SendInterval;
        FlushPending();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ST14TunerState tuner)
            _window?.UpdateState(tuner);
    }
}

/// <summary>
/// The colours of the field radio: an olive case, a green lit display
/// </summary>
public static class ST14RadioStyle
{
    public static readonly Color Case = Color.FromHex("#1E2420");
    public static readonly Color CaseEdge = Color.FromHex("#3E4A3C");
    public static readonly Color Plate = Color.FromHex("#283026");
    public static readonly Color Screen = Color.FromHex("#0A140C");
    public static readonly Color ScreenEdge = Color.FromHex("#2F5A33");
    public static readonly Color Lit = Color.FromHex("#7CFF8A");
    public static readonly Color LitDim = Color.FromHex("#2E6B36");
    public static readonly Color Amber = Color.FromHex("#FFC24A");
    public static readonly Color Red = Color.FromHex("#FF5A45");
    public static readonly Color Text = Color.FromHex("#C9D2C3");
    public static readonly Color Muted = Color.FromHex("#7D8A79");
    public static readonly Color Caption = Color.FromHex("#D4C48A");

    public static StyleBoxFlat Box(Color background, Color border, float borderWidth = 1f, float padding = 6f)
    {
        var box = new StyleBoxFlat { BackgroundColor = background, BorderColor = border, BorderThickness = new Thickness(borderWidth) };
        box.SetContentMarginOverride(StyleBox.Margin.All, padding);
        return box;
    }
}

/// <summary>
/// A hard key of the radio: dark, lighter under the cursor, lit while active
/// </summary>
public sealed class ST14RadioKey : ContainerButton
{
    // the base constructor already asks for a style, before the label exists
    private readonly Label? _label;
    private bool _active;
    private Color _accent = ST14RadioStyle.Lit;

    public ST14RadioKey(string text, Font font, float minWidth = 0f)
    {
        MinWidth = minWidth;
        MinHeight = 28;
        _label = new Label { Text = text, FontOverride = font, HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center };
        AddChild(_label);
        RestyleKey();
    }

    public string Text
    {
        get => _label?.Text ?? string.Empty;
        set => _label!.Text = value;
    }

    /// <summary>
    /// Lit like a selected setting
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            RestyleKey();
        }
    }

    public Color Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            RestyleKey();
        }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        RestyleKey();
    }

    private void RestyleKey()
    {
        if (_label == null)
            return;

        var (background, border, text) = (DrawMode, _active) switch
        {
            (DrawModeEnum.Disabled, _) => (ST14RadioStyle.Plate, ST14RadioStyle.CaseEdge, ST14RadioStyle.Muted),
            (_, true) => (_accent.WithAlpha(0.22f), _accent, _accent),
            (DrawModeEnum.Pressed, _) => (Color.FromHex("#151915"), ST14RadioStyle.LitDim, ST14RadioStyle.Lit),
            (DrawModeEnum.Hover, _) => (Color.FromHex("#36412F"), Color.FromHex("#6B7E5E"), Color.White),
            _ => (Color.FromHex("#2C3529"), ST14RadioStyle.CaseEdge, ST14RadioStyle.Text),
        };

        StyleBoxOverride = ST14RadioStyle.Box(background, border, 1f, 4f);
        _label.FontColorOverride = text;
    }
}

/// <summary>
/// A row of lamps: how strong the signal is, how far the lock went
/// </summary>
public sealed class ST14SegmentBar : Control
{
    public int Segments = 20;
    public float Value;
    public Color Low = ST14RadioStyle.Red;
    public Color Mid = ST14RadioStyle.Amber;
    public Color High = ST14RadioStyle.Lit;

    public ST14SegmentBar()
    {
        MinHeight = 14;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        if (size.X <= 0 || Segments <= 0)
            return;

        var gap = 2f * UIScale;
        var width = (size.X - gap * (Segments - 1)) / Segments;
        var lit = (int) MathF.Round(Math.Clamp(Value, 0f, 1f) * Segments);
        for (var i = 0; i < Segments; i++)
        {
            var share = (i + 1f) / Segments;
            var color = share <= 0.4f ? Low : share <= 0.8f ? Mid : High;
            var x = i * (width + gap);
            handle.DrawRect(new UIBox2(x, 0, x + width, size.Y), i < lit ? color : color.WithAlpha(0.12f));
        }
    }
}

/// <summary>
/// A round lamp on the case
/// </summary>
public sealed class ST14RadioLamp : Control
{
    public Color Color = ST14RadioStyle.Muted;

    public ST14RadioLamp()
    {
        MinSize = new Vector2(12, 12);
        VerticalAlignment = VAlignment.Center;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var radius = MathF.Min(PixelSize.X, PixelSize.Y) / 2f;
        var center = PixelSize / 2f;
        handle.DrawCircle(center, radius, Color.WithAlpha(0.25f));
        handle.DrawCircle(center, radius * 0.65f, Color);
    }
}

public sealed class ST14TunerWindow : DefaultWindow
{
    private readonly Font _small;
    private readonly Font _smallBold;

    private readonly ST14RadioLamp _lamp;
    private readonly Label _model;
    private readonly Label _frequency;
    private readonly Label _units;
    private readonly ST14TunerScope _scope;
    private readonly ST14SegmentBar _signalBar;
    private readonly Label _signal;
    private readonly Slider _slider;
    private readonly BoxContainer _lockBlock;
    private readonly ST14SegmentBar _lockBar;
    private readonly BoxContainer _packBlock;
    private readonly ST14RadioKey _power;
    private readonly ST14RadioKey[] _ranges = new ST14RadioKey[3];
    private readonly Label _rangeNote;
    private readonly ST14RadioKey[] _presets = new ST14RadioKey[3];
    private readonly Label _status;
    private readonly Label _help;

    private static readonly TimeSpan DialSettle = TimeSpan.FromSeconds(0.6);

    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private TimeSpan _lastTurn;
    private int _dial = ST14Frequency.Min;
    private bool _dialSet;
    private bool _pack;
    private bool _powered = true;

    public event Action<int>? OnDial;
    public event Action? OnFrame;
    public event Action<bool>? OnPower;
    public event Action<int>? OnRange;
    public event Action<int, bool>? OnPreset;

    public ST14TunerWindow()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        Font FontOf(string file, int size) => new VectorFont(cache.GetResource<FontResource>(file), size);
        var digits = FontOf("/Fonts/NotoSans/NotoSans-Bold.ttf", 34);
        var bold = FontOf("/Fonts/NotoSans/NotoSans-Bold.ttf", 13);
        _small = FontOf("/Fonts/NotoSans/NotoSans-Regular.ttf", 11);
        _smallBold = FontOf("/Fonts/NotoSans/NotoSans-Bold.ttf", 11);

        // the window takes the height of what it shows: the tower and the pack differ
        MinWidth = SetWidth = 500;

        var body = new PanelContainer { PanelOverride = ST14RadioStyle.Box(ST14RadioStyle.Case, ST14RadioStyle.CaseEdge, 2f, 10f), VerticalExpand = true };
        Contents.AddChild(body);

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        body.AddChild(column);

        // the plate of the case: the model and the lamp
        var head = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        _model = new Label { FontOverride = bold, FontColorOverride = ST14RadioStyle.Caption, HorizontalExpand = true };
        head.AddChild(_model);
        _lamp = new ST14RadioLamp();
        head.AddChild(_lamp);
        column.AddChild(head);

        // the display: the frequency in big lit digits, the wave and the lamps of the signal under it
        var screen = new PanelContainer { PanelOverride = ST14RadioStyle.Box(ST14RadioStyle.Screen, ST14RadioStyle.ScreenEdge, 2f, 8f) };
        var screenColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        screen.AddChild(screenColumn);

        var digitsRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalAlignment = HAlignment.Center, SeparationOverride = 6 };
        _frequency = new Label { FontOverride = digits, FontColorOverride = ST14RadioStyle.Lit };
        digitsRow.AddChild(_frequency);
        _units = new Label { Text = Loc.GetString("st14-tuner-mhz-unit"), FontOverride = bold, FontColorOverride = ST14RadioStyle.LitDim, VerticalAlignment = VAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
        digitsRow.AddChild(_units);
        screenColumn.AddChild(digitsRow);

        _scope = new ST14TunerScope { MinHeight = 48, HorizontalExpand = true };
        screenColumn.AddChild(_scope);

        _signalBar = new ST14SegmentBar();
        screenColumn.AddChild(_signalBar);
        _signal = new Label { FontOverride = _smallBold, HorizontalAlignment = HAlignment.Center };
        screenColumn.AddChild(_signal);
        column.AddChild(screen);

        // the dial: keys for tens, units and tenths, and the slider for the whole scale
        column.AddChild(Caption("st14-tuner-section-dial"));
        var keys = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var step in new[] { -100, -10, -1, 1, 10, 100 })
        {
            var key = new ST14RadioKey((step > 0 ? "+" : "−") + ST14Frequency.Format(Math.Abs(step)), bold) { HorizontalExpand = true };
            key.OnPressed += _ => SetDial(_dial + step);
            keys.AddChild(key);
        }
        column.AddChild(keys);

        _slider = new Slider { MinValue = ST14Frequency.Min, MaxValue = ST14Frequency.Max, HorizontalExpand = true, Rounded = true };
        _slider.OnValueChanged += range => SetDial((int) MathF.Round(range.Value), false);
        column.AddChild(_slider);

        var scale = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        foreach (var mark in new[] { 300, 450, 600, 750, 900 })
        {
            scale.AddChild(new Label
            {
                Text = ST14Frequency.Format(mark),
                FontOverride = _small,
                FontColorOverride = ST14RadioStyle.Muted,
                HorizontalExpand = true,
                Align = mark == 300 ? Label.AlignMode.Left : mark == 900 ? Label.AlignMode.Right : Label.AlignMode.Center,
            });
        }
        column.AddChild(scale);

        // on a tower: the lock
        _lockBlock = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        _lockBlock.AddChild(Caption("st14-tuner-lock"));
        _lockBar = new ST14SegmentBar { Segments = 12, Low = ST14RadioStyle.Amber, Mid = ST14RadioStyle.Amber, MinHeight = 16 };
        _lockBlock.AddChild(_lockBar);
        column.AddChild(_lockBlock);

        // on a pack: the power switch, the transmitter, the memory
        _packBlock = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };

        var powerRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        var powerColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, MinWidth = 110 };
        powerColumn.AddChild(Caption("st14-tuner-section-power"));
        _power = new ST14RadioKey(string.Empty, bold) { HorizontalExpand = true };
        _power.OnPressed += _ => OnPower?.Invoke(!_powered);
        powerColumn.AddChild(_power);
        powerRow.AddChild(powerColumn);

        var rangeColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
        rangeColumn.AddChild(Caption("st14-tuner-section-range"));
        var rangeKeys = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        for (var i = 0; i < _ranges.Length; i++)
        {
            var power = i;
            _ranges[i] = new ST14RadioKey(Loc.GetString($"st14-tuner-range-{i}"), bold) { HorizontalExpand = true, Accent = i == 2 ? ST14RadioStyle.Amber : ST14RadioStyle.Lit };
            _ranges[i].OnPressed += _ => OnRange?.Invoke(power);
            rangeKeys.AddChild(_ranges[i]);
        }
        rangeColumn.AddChild(rangeKeys);
        powerRow.AddChild(rangeColumn);
        _packBlock.AddChild(powerRow);

        _rangeNote = new Label { FontOverride = _small, FontColorOverride = ST14RadioStyle.Muted };
        _packBlock.AddChild(_rangeNote);

        _packBlock.AddChild(Caption("st14-tuner-section-memory"));
        var memory = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        for (var i = 0; i < _presets.Length; i++)
        {
            var slot = i;
            var cell = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2, HorizontalExpand = true };
            _presets[i] = new ST14RadioKey(string.Empty, bold) { HorizontalExpand = true };
            _presets[i].OnPressed += _ => OnPreset?.Invoke(slot, false);
            cell.AddChild(_presets[i]);
            var save = new ST14RadioKey(Loc.GetString("st14-tuner-memory-save"), _small) { HorizontalExpand = true, MinHeight = 20 };
            save.OnPressed += _ => OnPreset?.Invoke(slot, true);
            cell.AddChild(save);
            memory.AddChild(cell);
        }
        _packBlock.AddChild(memory);
        column.AddChild(_packBlock);

        // the status and how to use it
        var statusPlate = new PanelContainer { PanelOverride = ST14RadioStyle.Box(ST14RadioStyle.Plate, ST14RadioStyle.CaseEdge, 1f, 6f) };
        _status = new Label { FontOverride = bold };
        statusPlate.AddChild(_status);
        column.AddChild(statusPlate);

        _help = new Label { FontOverride = _small, FontColorOverride = ST14RadioStyle.Muted, VerticalAlignment = VAlignment.Top };
        column.AddChild(_help);

        _packBlock.Visible = false;
        ShowDial();
    }

    private Label Caption(string loc)
    {
        return new Label { Text = Loc.GetString(loc).ToUpperInvariant(), FontOverride = _smallBold, FontColorOverride = ST14RadioStyle.Caption };
    }

    private void SetDial(int dial, bool moveSlider = true)
    {
        dial = Math.Clamp(dial, ST14Frequency.Min, ST14Frequency.Max);
        if (dial == _dial)
            return;

        _dial = dial;
        _lastTurn = _timing.RealTime;
        if (moveSlider)
            _slider.SetValueWithoutEvent(dial);

        ShowDial();
        OnDial?.Invoke(dial);
    }

    private void ShowDial()
    {
        _frequency.Text = ST14Frequency.Format(_dial);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        OnFrame?.Invoke();
    }

    public void UpdateState(ST14TunerState state)
    {
        var first = !_dialSet;
        if (first)
        {
            _dialSet = true;
            _pack = state.Pack;
            Title = Loc.GetString(_pack ? "st14-tuner-title-pack" : "st14-tuner-title");
            _model.Text = Loc.GetString(_pack ? "st14-tuner-model-pack" : "st14-tuner-model-tower");
            _help.Text = Loc.GetString(_pack ? "st14-tuner-steps-pack" : "st14-tuner-steps");
            _lockBlock.Visible = !_pack;
            _packBlock.Visible = _pack;
        }

        // the dial is the signaller's. The server only moves it on a pack, when a memory cell was recalled, and only
        // once the turns of the signaller had time to reach it
        if (first || _pack && state.Dial != _dial && _timing.RealTime - _lastTurn > DialSettle)
            PutDial(state.Dial);

        _scope.Signal = state.Signal;
        _signalBar.Value = state.Signal;

        if (_pack)
            UpdatePack(state);
        else
            UpdateTower(state);
    }

    private void PutDial(int dial)
    {
        _dial = Math.Clamp(dial, ST14Frequency.Min, ST14Frequency.Max);
        _slider.SetValueWithoutEvent(_dial);
        ShowDial();
    }

    private void Show(Label label, string loc, Color color)
    {
        label.Text = Loc.GetString(loc);
        label.FontColorOverride = color;
    }

    private void UpdateTower(ST14TunerState state)
    {
        var caught = state.Lock >= 1f && state.TowerFrequency != null;
        _lockBar.Value = state.Lock;
        _lamp.Color = caught ? ST14RadioStyle.Lit : state.Signal >= 0.985f ? ST14RadioStyle.Amber : ST14RadioStyle.Red;

        switch (state.Signal)
        {
            case var _ when caught:
                Show(_signal, "st14-tuner-signal-caught", ST14RadioStyle.Lit);
                break;
            case >= 0.985f:
                Show(_signal, "st14-tuner-signal-on", ST14RadioStyle.Lit);
                break;
            case >= 0.75f:
                Show(_signal, "st14-tuner-signal-close", ST14RadioStyle.Amber);
                break;
            case >= 0.05f:
                Show(_signal, "st14-tuner-signal-weak", ST14RadioStyle.Amber);
                break;
            default:
                Show(_signal, "st14-tuner-signal-none", ST14RadioStyle.Muted);
                break;
        }

        if (caught)
        {
            _status.Text = Loc.GetString("st14-tuner-caught", ("frequency", ST14Frequency.Format(state.TowerFrequency!.Value)));
            _status.FontColorOverride = ST14RadioStyle.Lit;
        }
        else if (state.TowerFrequency is { } held)
        {
            _status.Text = Loc.GetString("st14-tuner-tower-holds", ("frequency", ST14Frequency.Format(held)), ("percent", Percent(state.Quality)));
            _status.FontColorOverride = ST14RadioStyle.Text;
        }
        else
        {
            Show(_status, "st14-tuner-no-link", ST14RadioStyle.Red);
        }
    }

    private void UpdatePack(ST14TunerState state)
    {
        _powered = state.Powered;
        _power.Text = Loc.GetString(_powered ? "st14-tuner-power-on" : "st14-tuner-power-off");
        _power.Active = _powered;

        for (var i = 0; i < _ranges.Length; i++)
        {
            _ranges[i].Active = i == state.Power;
        }

        _rangeNote.Text = Loc.GetString("st14-tuner-range-note", ("tiles", (int) state.Range));
        _rangeNote.FontColorOverride = ST14RadioStyle.Muted;

        for (var i = 0; i < _presets.Length && i < state.Presets.Length; i++)
        {
            _presets[i].Text = Loc.GetString("st14-tuner-memory-cell", ("slot", i + 1), ("frequency", ST14Frequency.Format(state.Presets[i])));
            _presets[i].Active = state.Presets[i] == _dial;
        }

        // switched off the display goes dark
        _frequency.FontColorOverride = _powered ? ST14RadioStyle.Lit : ST14RadioStyle.LitDim;
        _scope.Dark = !_powered;

        var on = state.Lock >= 1f;
        _lamp.Color = !_powered ? ST14RadioStyle.Muted : on ? ST14RadioStyle.Lit : ST14RadioStyle.Red;

        if (!_powered)
        {
            Show(_signal, "st14-tuner-pack-off", ST14RadioStyle.Muted);
            Show(_status, "st14-tuner-pack-status-off", ST14RadioStyle.Muted);
            return;
        }

        switch (state.Signal)
        {
            case var _ when on:
                Show(_signal, "st14-tuner-pack-on", ST14RadioStyle.Lit);
                break;
            case >= 0.75f:
                Show(_signal, "st14-tuner-signal-close", ST14RadioStyle.Amber);
                break;
            case >= 0.05f:
                Show(_signal, "st14-tuner-signal-weak", ST14RadioStyle.Amber);
                break;
            default:
                Show(_signal, "st14-tuner-signal-none", ST14RadioStyle.Muted);
                break;
        }

        if (on)
        {
            _status.Text = Loc.GetString("st14-tuner-pack-linked", ("percent", Percent(state.Quality)));
            _status.FontColorOverride = ST14RadioStyle.Lit;
        }
        else
        {
            Show(_status, "st14-tuner-pack-not-linked", ST14RadioStyle.Red);
        }
    }

    private static int Percent(float value)
    {
        return (int) MathF.Round(value * 100f);
    }
}

/// <summary>
/// The screen of the tuner: static where the dial is off, a steady green wave as it comes onto the frequency
/// </summary>
public sealed class ST14TunerScope : Control
{
    private readonly IRobustRandom _random = IoCManager.Resolve<IRobustRandom>();
    private float _time;

    public float Signal;
    public bool Dark;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var middle = size.Y / 2f;

        // the grid of the display
        var grid = ST14RadioStyle.LitDim.WithAlpha(0.25f);
        if (size.X <= 0 || size.Y <= 0)
            return;

        for (var i = 0; i <= 10; i++)
        {
            var x = size.X * i / 10f;
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), grid);
        }
        handle.DrawLine(new Vector2(0, middle), new Vector2(size.X, middle), grid);

        if (Dark)
            return;

        var noise = 1f - Signal;
        var wave = Color.InterpolateBetween(ST14RadioStyle.Red, ST14RadioStyle.Lit, Signal);
        var previous = new Vector2(0, middle);
        for (var x = 2; x <= size.X; x += 3)
        {
            var clean = MathF.Sin(x * 0.06f + _time * 6f) * middle * 0.6f * Signal;
            var hiss = (_random.NextFloat() * 2f - 1f) * middle * 0.85f * noise;
            var point = new Vector2(x, middle - clean - hiss);
            handle.DrawLine(previous, point, wave);
            previous = point;
        }
    }
}
