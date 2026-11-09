using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Shared._ST14.Medicine;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// The screen of the field scanner: the patient as a doll lit part by part with the marks of the medicine on it,
/// a running cardiogram that beeps with every beat and drones flat when the heart stops, the blood, the pain,
/// the load on the heart, the drugs in the blood against their overdose, and what will kill the patient first
/// </summary>
public sealed class ST14MedScannerWindow : DefaultWindow
{
    private static readonly Color Panel = Color.FromHex("#0E1412");
    private static readonly Color Text = Color.FromHex("#D6DDD0");
    private static readonly Color Muted = Color.FromHex("#7E8C84");
    private static readonly Color Danger = Color.FromHex("#FF5A4A");
    private static readonly Color Good = Color.FromHex("#6FE08A");
    private static readonly Color Infected = Color.FromHex("#B5D94A");

    private readonly IResourceCache _cache;
    private readonly IPrototypeManager _prototypes;

    private readonly Label _name;
    private readonly Label _state;
    private readonly ST14BodyDoll _doll;
    private readonly ST14Cardiogram _cardiogram;
    private readonly Label _pulse;
    private readonly ST14MedBar _blood;
    private readonly ST14MedBar _pain;
    private readonly ST14MedBar _load;
    private readonly Label _bleeding;
    private readonly BoxContainer _warnings;
    private readonly Label _warningsTitle;
    private readonly ScrollContainer _warningsScroll;
    private readonly BoxContainer _wounds;
    private readonly BoxContainer _drugs;
    private readonly Label _range;

    public ST14MedScannerWindow()
    {
        _cache = IoCManager.Resolve<IResourceCache>();
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        var entities = IoCManager.Resolve<IEntityManager>();

        Title = Loc.GetString("st14-scan-title");
        MinSize = new Vector2(1080, 720);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, Margin = new Thickness(8) };
        Contents.AddChild(root);

        // the patient
        var left = new PanelContainer { PanelOverride = ST14HubStyle.Flat(Panel, ST14HubStyle.Gold.WithAlpha(0.4f), 1), MinWidth = 250 };
        var leftColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(8) };
        left.AddChild(leftColumn);
        _name = ST14HubStyle.Label(_cache, string.Empty, ST14HubFont.Bold, 18, ST14HubStyle.GoldLight, true);
        _name.ClipText = true;
        _state = ST14HubStyle.Label(_cache, string.Empty, ST14HubFont.SemiBold, 14, Good, true);
        _doll = new ST14BodyDoll(entities.System<SpriteSystem>()) { MinSize = new Vector2(230, 430), VerticalExpand = true };
        _range = ST14HubStyle.Label(_cache, string.Empty, ST14HubFont.Medium, 12, Danger, true);
        leftColumn.AddChild(_name);
        leftColumn.AddChild(_state);
        leftColumn.AddChild(_doll);
        leftColumn.AddChild(_range);
        root.AddChild(left);

        var right = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
        root.AddChild(right);

        // the heart
        var heart = new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black, Good.WithAlpha(0.35f), 1) };
        var heartRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, Margin = new Thickness(6) };
        heart.AddChild(heartRow);
        _cardiogram = new ST14Cardiogram { MinHeight = 90, HorizontalExpand = true };
        var pulseColumn = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 120 };
        pulseColumn.AddChild(ST14HubStyle.Label(_cache, Loc.GetString("st14-scan-pulse"), ST14HubFont.SemiBold, 12, Muted));
        _pulse = ST14HubStyle.Label(_cache, "--", ST14HubFont.Black, 36, Good, true);
        pulseColumn.AddChild(_pulse);
        heartRow.AddChild(_cardiogram);
        heartRow.AddChild(pulseColumn);
        right.AddChild(heart);

        // the vitals
        var vitals = new GridContainer { Columns = 2, HSeparationOverride = 10, VSeparationOverride = 4 };
        _blood = AddBar(vitals, "st14-scan-blood");
        _pain = AddBar(vitals, "st14-scan-pain");
        _load = AddBar(vitals, "st14-scan-heart-load");
        vitals.AddChild(ST14HubStyle.Label(_cache, Loc.GetString("st14-scan-bleeding"), ST14HubFont.SemiBold, 13, Muted));
        _bleeding = ST14HubStyle.Label(_cache, string.Empty, ST14HubFont.Bold, 14, Text);
        vitals.AddChild(_bleeding);
        right.AddChild(vitals);

        // what is dangerous right now
        _warningsTitle = ST14HubStyle.Label(_cache, Loc.GetString("st14-scan-warnings"), ST14HubFont.Bold, 14, Danger);
        _warningsTitle.Margin = new Thickness(0, 10, 0, 0);
        right.AddChild(_warningsTitle);
        _warnings = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        _warningsScroll = new ScrollContainer { HScrollEnabled = false, SetHeight = 118 };
        _warningsScroll.AddChild(_warnings);
        right.AddChild(_warningsScroll);

        // wounds and drugs side by side
        var lists = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 12, VerticalExpand = true };
        right.AddChild(lists);
        _wounds = AddList(lists, "st14-scan-wounds");
        _drugs = AddList(lists, "st14-scan-drugs");

        _cardiogram.OnBeat += () => Play(entities, "/Audio/_ST14/Medicine/ekg_beep.wav", -6f);
        _cardiogram.OnFlatline += () => Play(entities, "/Audio/_ST14/Medicine/flatline.wav", -8f);
    }

    private static void Play(IEntityManager entities, string path, float volume)
    {
        entities.System<SharedAudioSystem>().PlayGlobal(new SoundPathSpecifier(path), Filter.Local(), false, AudioParams.Default.WithVolume(volume));
    }

    private ST14MedBar AddBar(GridContainer grid, string key)
    {
        grid.AddChild(ST14HubStyle.Label(_cache, Loc.GetString(key), ST14HubFont.SemiBold, 13, Muted));
        var bar = new ST14MedBar { MinSize = new Vector2(300, 16), HorizontalExpand = true };
        grid.AddChild(bar);
        return bar;
    }

    private BoxContainer AddList(BoxContainer parent, string key)
    {
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2 };
        column.AddChild(ST14HubStyle.Label(_cache, Loc.GetString(key), ST14HubFont.Bold, 14, ST14HubStyle.GoldLight));
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, MinHeight = 140 };
        var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        scroll.AddChild(list);
        column.AddChild(scroll);
        parent.AddChild(column);
        return list;
    }

    private Label Line(string text, Color color, int size = 13)
    {
        var label = ST14HubStyle.Label(_cache, text, ST14HubFont.Medium, size, color);
        label.ClipText = true;
        return label;
    }

    public void Update(ST14MedScannerState state)
    {
        _name.Text = state.Name;
        var arrest = state.Heart == ST14HeartState.Arrest && state.MobState != 2;
        (_state.Text, _state.FontColorOverride) = state.MobState switch
        {
            2 => (Loc.GetString("st14-scan-state-dead"), Muted),
            _ when arrest => (Loc.GetString("st14-scan-state-arrest"), Danger),
            1 => (Loc.GetString("st14-scan-state-crit"), ST14HubStyle.Orange),
            _ => (Loc.GetString("st14-scan-state-alive"), Good),
        };

        _range.Text = state.InRange ? string.Empty : Loc.GetString("st14-scan-out-of-range");
        _doll.Update(state);
        _cardiogram.Update(state.MobState == 2 ? 0 : state.Pulse, arrest || state.MobState == 2, state.Heart == ST14HeartState.Arrhythmia);

        _pulse.Text = arrest || state.MobState == 2 ? "0" : state.Pulse.ToString();
        _pulse.FontColorOverride = arrest ? Danger : state.Pulse >= 130 ? ST14HubStyle.Orange : Good;

        _blood.Set(state.Blood, state.Blood < 0.6f ? Danger : state.Blood < 0.85f ? ST14HubStyle.Orange : Good, $"{state.Blood * 100f:0}%");
        _pain.Set(state.Pain / 100f, state.Pain >= 75f ? Danger : state.Pain >= 40f ? ST14HubStyle.Orange : Good, $"{state.Pain:0}");
        _load.Set(Math.Clamp(state.HeartLoad, 0f, 1f), state.HeartLoad >= 0.9f ? Danger : state.HeartLoad >= 0.6f ? ST14HubStyle.Orange : Good, $"{state.HeartLoad * 100f:0}%");
        _bleeding.Text = Loc.GetString("st14-scan-per-second", ("value", state.Bleeding.ToString("0.00")));
        _bleeding.FontColorOverride = state.Bleeding >= 1f ? Danger : state.Bleeding > 0.05f ? ST14HubStyle.Orange : Good;

        // nothing threatens the patient: no red block at all
        _warnings.RemoveAllChildren();
        _warningsTitle.Visible = state.Warnings.Length > 0;
        _warningsScroll.Visible = state.Warnings.Length > 0;

        foreach (var warning in state.Warnings)
        {
            _warnings.AddChild(Line("! " + warning, Danger, 14));
        }

        _wounds.RemoveAllChildren();
        var any = false;
        foreach (var zone in ST14Zones.All)
        {
            var name = ST14Zones.Name(zone);
            if (state.Missing.Contains(zone) && state.Wounds.All(w => w.Zone != zone))
            {
                _wounds.AddChild(Line($"{name}: {Loc.GetString("st14-scan-missing")}", Muted));
                any = true;
                continue;
            }

            var parts = new List<string>();
            foreach (var wound in state.Wounds.Where(w => w.Zone == zone))
            {
                var text = Loc.GetString($"st14-scan-wound-{wound.Kind.ToString().ToLowerInvariant()}");
                var notes = new List<string>();
                if (wound.Arterial)
                    notes.Add(Loc.GetString("st14-scan-wound-arterial"));
                if (wound.Sutured)
                    notes.Add(Loc.GetString("st14-scan-wound-sutured"));
                else if (wound.Packed)
                    notes.Add(Loc.GetString("st14-scan-wound-packed"));
                else if (wound.Bandaged)
                    notes.Add(Loc.GetString("st14-scan-wound-bandaged"));
                if (wound.Splinted)
                    notes.Add(Loc.GetString("st14-scan-wound-splinted"));
                if (wound.Infection >= 0.1f)
                    notes.Add(Loc.GetString("st14-scan-wound-infected", ("percent", (int) (wound.Infection * 100f))));
                else if (wound.Clean)
                    notes.Add(Loc.GetString("st14-scan-wound-clean"));
                else if (wound.Dirty && ST14Zones.IsOpen(wound.Kind) && !wound.Sutured)
                    notes.Add(Loc.GetString("st14-scan-wound-dirty"));
                if (wound.Kind == ST14WoundKind.Fracture && wound.Severity < 1f)
                    notes.Add(Loc.GetString("st14-scan-wound-healing", ("percent", (int) ((1f - wound.Severity) * 100f))));

                parts.Add(notes.Count > 0 ? $"{text} ({string.Join(", ", notes)})" : text);
            }

            if (state.Tourniquets.TryGetValue(zone, out var seconds))
                parts.Add(Loc.GetString("st14-scan-tourniquet", ("minutes", (int) (seconds / 60f)), ("seconds", ((int) seconds % 60).ToString("00"))));

            if (parts.Count == 0)
                continue;

            any = true;
            var bleeding = state.Wounds.Any(w => w.Zone == zone && w.Bleed > 0.05f && !w.Sutured && !state.Tourniquets.ContainsKey(zone));
            var dying = seconds >= state.IschemiaTime;
            var infected = state.Wounds.Any(w => w.Zone == zone && w.Infection >= 0.3f);
            _wounds.AddChild(Line($"{name}: {string.Join(", ", parts)}", dying || bleeding ? Danger : infected ? Infected : Text));
        }

        if (!any)
            _wounds.AddChild(Line(Loc.GetString("st14-scan-no-wounds"), Good));

        _drugs.RemoveAllChildren();
        if (state.Drugs.Length == 0)
            _drugs.AddChild(Line(Loc.GetString("st14-scan-no-drugs"), Muted));

        foreach (var drug in state.Drugs)
        {
            var name = _prototypes.TryIndex<ReagentPrototype>(drug.Reagent, out var reagent) ? reagent.LocalizedName : drug.Reagent;
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            var label = Line($"{name} {drug.Amount:0.#}", drug.Strength >= 0.8f ? Danger : Text);
            label.MinWidth = 170;
            row.AddChild(label);
            if (drug.Overdose > 0f)
            {
                var bar = new ST14MedBar { MinSize = new Vector2(90, 10), VerticalAlignment = VAlignment.Center };
                bar.Set(Math.Clamp(drug.Strength, 0f, 1f), drug.Strength >= 0.8f ? Danger : drug.Strength >= 0.5f ? ST14HubStyle.Orange : Good, null);
                row.AddChild(bar);
            }

            _drugs.AddChild(row);

            // what it does, small under the name: the medic learns the drugs from the scanner
            if (drug.Summary is { } summary)
            {
                var what = Line(Loc.GetString(summary), Muted, 11);
                what.Margin = new Thickness(10, 0, 0, 2);
                _drugs.AddChild(what);
            }
        }
    }
}

/// <summary>
/// A thin bar with its value written over it
/// </summary>
public sealed class ST14MedBar : Control
{
    private float _value;
    private Color _color = Color.White;
    private string? _text;

    public void Set(float value, Color color, string? text)
    {
        _value = Math.Clamp(value, 0f, 1f);
        _color = color;
        _text = text;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = new UIBox2(Vector2.Zero, PixelSize);
        handle.DrawRect(box, Color.FromHex("#1B2420"));
        handle.DrawRect(new UIBox2(0, 0, PixelSize.X * _value, PixelSize.Y), _color.WithAlpha(0.85f));
        handle.DrawRect(box, Color.FromHex("#3C4A44"), false);
        if (_text == null)
            return;

        var font = UserInterfaceManager.ThemeDefaults.DefaultFont;
        var size = handle.GetDimensions(font, _text, UIScale);
        handle.DrawString(font, new Vector2(PixelSize.X - size.X - 4, (PixelSize.Y - size.Y) / 2f), _text, UIScale, Color.White);
    }
}

/// <summary>
/// The running trace of the heart: a spike every beat, beeping; uneven when it is out of rhythm; flat and droning when
/// it stops
/// </summary>
public sealed class ST14Cardiogram : Control
{
    private const int Samples = 260;
    private const float SamplesPerSecond = 65f;

    private readonly float[] _trace = new float[Samples];
    private int _head;
    private float _carry;
    private float _phase;
    private int _pulse;
    private bool _flat;
    private bool _erratic;
    private float _flatTimer;
    private readonly Random _random = new();

    public event Action? OnBeat;
    public event Action? OnFlatline;

    public void Update(int pulse, bool flat, bool erratic)
    {
        _pulse = pulse;
        if (flat && !_flat)
            _flatTimer = 0f;

        _flat = flat;
        _erratic = erratic;
    }

    /// <summary>
    /// The shape of one beat: a small P wave, the tall spike of QRS and a round T wave
    /// </summary>
    private static float Wave(float p)
    {
        if (p is > 0.10f and < 0.18f)
            return 0.12f * MathF.Sin(MathF.PI * (p - 0.10f) / 0.08f);
        if (p is >= 0.22f and < 0.24f)
            return -0.15f * (p - 0.22f) / 0.02f;
        if (p is >= 0.24f and < 0.255f)
            return -0.15f + 1.15f * (p - 0.24f) / 0.015f;
        if (p is >= 0.255f and < 0.27f)
            return 1f - 1.3f * (p - 0.255f) / 0.015f;
        if (p is >= 0.27f and < 0.30f)
            return -0.3f + 0.3f * (p - 0.27f) / 0.03f;
        if (p is > 0.40f and < 0.55f)
            return 0.25f * MathF.Sin(MathF.PI * (p - 0.40f) / 0.15f);
        return 0f;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_flat)
        {
            _flatTimer -= args.DeltaSeconds;
            if (_flatTimer <= 0f)
            {
                _flatTimer = 2.6f;
                OnFlatline?.Invoke();
            }
        }

        _carry += args.DeltaSeconds * SamplesPerSecond;
        while (_carry >= 1f)
        {
            _carry -= 1f;
            var value = 0f;
            if (!_flat && _pulse > 0)
            {
                var before = _phase;
                var rate = _pulse / 60f * (_erratic ? 0.6f + (float) _random.NextDouble() * 0.9f : 1f);
                _phase += rate / SamplesPerSecond;
                if (before < 0.25f && _phase >= 0.25f)
                    OnBeat?.Invoke();

                if (_phase >= 1f)
                    _phase -= 1f;

                value = Wave(_phase);
            }

            value += ((float) _random.NextDouble() - 0.5f) * 0.03f;
            _trace[_head] = value;
            _head = (_head + 1) % Samples;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        handle.DrawRect(new UIBox2(Vector2.Zero, size), Color.FromHex("#020704"));

        // the grid of the monitor
        var grid = Color.FromHex("#0D2A16");
        for (var x = 0f; x < size.X; x += size.Y / 4f)
        {
            handle.DrawLine(new Vector2(x, 0), new Vector2(x, size.Y), grid);
        }

        for (var y = 0f; y < size.Y; y += size.Y / 4f)
        {
            handle.DrawLine(new Vector2(0, y), new Vector2(size.X, y), grid);
        }

        var colour = _flat ? Color.FromHex("#FF4A3A") : Color.FromHex("#4DFF7A");
        var middle = size.Y * 0.62f;
        var scale = size.Y * 0.5f;
        Vector2? last = null;
        for (var i = 0; i < Samples; i++)
        {
            var value = _trace[(_head + i) % Samples];
            var point = new Vector2(i / (float) (Samples - 1) * size.X, middle - value * scale);
            if (last is { } from)
                handle.DrawLine(from, point, colour);

            last = point;
        }

        // the bright dot writing the trace
        if (last is { } tip)
            handle.DrawCircle(tip, 3f, Color.White);
    }
}

/// <summary>
/// The patient as a doll: every part lit by how it is, the marks of the medicine drawn on it, the lost limbs ghosted
/// </summary>
public sealed class ST14BodyDoll : Control
{
    private static readonly ResPath PartsRsi = new("/Textures/Mobs/Species/Human/parts.rsi");
    private static readonly ResPath MarksRsi = new("/Textures/_ST14/Mobs/medical_overlays.rsi");

    private static readonly (ST14BodyZone Zone, string[] States, string Mark)[] Layout =
    {
        (ST14BodyZone.Torso, new[] { "torso_m" }, "torso"),
        (ST14BodyZone.LegLeft, new[] { "l_leg", "l_foot" }, "l_leg"),
        (ST14BodyZone.LegRight, new[] { "r_leg", "r_foot" }, "r_leg"),
        (ST14BodyZone.ArmLeft, new[] { "l_arm", "l_hand" }, "l_arm"),
        (ST14BodyZone.ArmRight, new[] { "r_arm", "r_hand" }, "r_arm"),
        (ST14BodyZone.Head, new[] { "head_m" }, "head"),
    };

    private readonly SpriteSystem _sprite;
    private ST14MedScannerState? _state;

    public ST14BodyDoll(SpriteSystem sprite)
    {
        _sprite = sprite;
    }

    public void Update(ST14MedScannerState state)
    {
        _state = state;
    }

    private Color ColourOf(ST14MedScannerState state, ST14BodyZone zone, float time)
    {
        if (state.MobState == 2)
            return Color.FromHex("#5C5C5C");

        var wounds = state.Wounds.Where(w => w.Zone == zone).ToList();
        var tourniquet = state.Tourniquets.TryGetValue(zone, out var seconds);
        var bleeding = tourniquet ? 0f : wounds.Where(w => !w.Sutured).Sum(w => w.Packed ? w.Bleed * 0.12f : w.Bandaged ? w.Bleed * 0.3f : w.Bleed);
        var pulse = 0.5f + 0.5f * MathF.Sin(time * 7f);

        if (bleeding >= 1f)
            return Color.InterpolateBetween(Color.FromHex("#B01010"), Color.FromHex("#FF4040"), pulse);
        if (tourniquet && seconds >= state.IschemiaTime)
            return Color.InterpolateBetween(Color.FromHex("#5A4AA8"), Color.FromHex("#9080E0"), pulse);
        if (bleeding > 0.05f)
            return Color.FromHex("#E0562E");
        if (wounds.Any(w => w.Infection >= 0.3f))
            return Color.InterpolateBetween(Color.FromHex("#7FAF2E"), Color.FromHex("#C8F060"), wounds.Any(w => w.Infection >= 0.8f) ? pulse : 0f);
        if (wounds.Any(w => w.Kind == ST14WoundKind.Fracture && !w.Splinted || w.Kind == ST14WoundKind.Malunion))
            return Color.FromHex("#E8C23A");
        if (wounds.Any(w => !w.Sutured && !w.Bandaged && !w.Packed && w.Kind != ST14WoundKind.Fracture && w.Severity > 0.25f))
            return Color.FromHex("#D9893A");
        if (wounds.Count > 0 || tourniquet)
            return Color.FromHex("#5FB8C9");

        return Color.FromHex("#6FBF73");
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_state is not { } state)
            return;

        var time = (float) IoCManager.Resolve<IGameTiming>().RealTime.TotalSeconds;
        var side = MathF.Min(PixelSize.X, PixelSize.Y);
        var box = UIBox2.FromDimensions(new Vector2((PixelSize.X - side) / 2f, (PixelSize.Y - side) / 2f), new Vector2(side, side));

        foreach (var (zone, states, mark) in Layout)
        {
            var missing = state.Missing.Contains(zone);
            var colour = missing ? Color.White.WithAlpha(0.08f) : ColourOf(state, zone, time);
            foreach (var part in states)
            {
                handle.DrawTextureRect(_sprite.Frame0(new SpriteSpecifier.Rsi(PartsRsi, part)), box, colour);
            }

            if (missing)
                continue;

            if (state.Wounds.Any(w => w.Zone == zone && (w.Bandaged || w.Packed)))
                handle.DrawTextureRect(_sprite.Frame0(new SpriteSpecifier.Rsi(MarksRsi, $"bandage_{mark}")), box);

            if (state.Wounds.Any(w => w.Zone == zone && w.Kind == ST14WoundKind.Fracture && w.Splinted))
                handle.DrawTextureRect(_sprite.Frame0(new SpriteSpecifier.Rsi(MarksRsi, $"splint_{mark}")), box);

            if (state.Tourniquets.ContainsKey(zone))
                handle.DrawTextureRect(_sprite.Frame0(new SpriteSpecifier.Rsi(MarksRsi, $"tourniquet_{mark}")), box);
        }
    }
}
