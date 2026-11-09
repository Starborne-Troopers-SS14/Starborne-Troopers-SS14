using System.Numerics;
using Content.Client.Resources;
using Content.Shared._ST14.Input;
using Content.Shared._ST14.Medicine;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// What the two players of a rescue see: the heart monitor of the downed one, with the beats running at a line
/// to press on, and the charge bar of the medic with the green zone to fire in
/// </summary>
public sealed partial class ST14MedicineOverlay : Overlay
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";
    private const string BoldPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const float MonitorWidth = 520f;
    private const float MonitorHeight = 110f;
    private const float PixelsPerSecond = 170f;
    private const float PressLine = 0.25f;
    private const float BarWidth = 320f;
    private const float BarHeight = 22f;
    private const float BottomMargin = 150f;

    /// <summary>
    /// How high the monitor with its hint reaches from the bottom of the screen, for whatever is drawn above it
    /// </summary>
    public const float MonitorReach = BottomMargin + MonitorHeight + 40f;

    private static readonly Color Back = Color.Black.WithAlpha(0.75f);
    private static readonly Color Glass = Color.FromHex("#02100A").WithAlpha(0.9f);
    private static readonly Color Trace = Color.FromHex("#4CE08A");
    private static readonly Color Good = Color.FromHex("#7CFF9E");
    private static readonly Color Bad = Color.FromHex("#FF5A4A");
    private static readonly Color Gold = Color.FromHex("#F2C14E");
    private static readonly Color Hot = Color.FromHex("#E8743B");

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _cache = default!;

    private readonly ST14FieldDefibSystem _defib;

    // made on the first draw: a client without graphics, like the test one, never needs it
    private Font? _font;
    private Font? _bold;

    // the beats already gone by, to keep drawing them where they were
    private readonly List<(TimeSpan At, Color Color, bool Tall)> _past = new();
    private readonly List<(float At, Color Color, bool Tall)> _beats = new();
    private TimeSpan _feedbackAt;
    private TimeSpan _lastNext;
    private ST14BeatResult _lastResult;

    private static Color ColorOf(ST14BeatResult result)
    {
        return result switch
        {
            ST14BeatResult.Hit => Good,
            ST14BeatResult.Miss => Bad,
            _ => Trace,
        };
    }

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public ST14MedicineOverlay()
    {
        IoCManager.InjectDependencies(this);
        _defib = _entities.System<ST14FieldDefibSystem>();
        ZIndex = 60;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } local)
            return;

        var bounds = args.ViewportBounds;
        var bottom = new Vector2((bounds.Left + bounds.Right) / 2f, bounds.Bottom - BottomMargin);

        if (_entities.TryGetComponent(local, out ST14DyingComponent? dying))
            DrawMonitor(args.ScreenHandle, bottom, dying);

        var defibs = _entities.EntityQueryEnumerator<ST14FieldDefibComponent>();
        while (defibs.MoveNext(out var defib))
        {
            if (defib.User == local && defib.Target != null)
                DrawCharge(args.ScreenHandle, bottom, defib);
        }
    }

    private void DrawMonitor(DrawingHandleScreen handle, Vector2 bottom, ST14DyingComponent dying)
    {
        var box = UIBox2.FromDimensions(bottom.X - MonitorWidth / 2f, bottom.Y - MonitorHeight, MonitorWidth, MonitorHeight);

        // every beat keeps its own colour as it scrolls past: green when caught, red when missed
        if (dying.NextBeat != _lastNext)
        {
            if (_lastNext != TimeSpan.Zero)
                _past.Add((_lastNext, ColorOf(_lastResult), false));

            _lastNext = dying.NextBeat;
        }

        if (dying.Result != ST14BeatResult.None && _lastResult == ST14BeatResult.None)
            _feedbackAt = _timing.RealTime;

        _lastResult = dying.Result;

        var now = _timing.CurTime;
        _past.RemoveAll(b => (now - b.At).TotalSeconds > 4);

        // the screen of a monitor: dark glass, a fine grid, a coarser one
        handle.DrawRect(box, Glass);
        for (var x = box.Left + 10f; x < box.Right; x += 10f)
        {
            var major = (int) MathF.Round((x - box.Left) / 10f) % 5 == 0;
            handle.DrawLine(new Vector2(x, box.Top), new Vector2(x, box.Bottom), Trace.WithAlpha(major ? 0.16f : 0.06f));
        }

        for (var y = box.Top + 10f; y < box.Bottom; y += 10f)
        {
            var major = (int) MathF.Round((y - box.Top) / 10f) % 5 == 0;
            handle.DrawLine(new Vector2(box.Left, y), new Vector2(box.Right, y), Trace.WithAlpha(major ? 0.16f : 0.06f));
        }

        // where to press: a band as wide as the window around the line
        var line = box.Left + MonitorWidth * PressLine;
        var window = (dying.ReviveBeat ? dying.ReviveWindow : dying.Window) * PixelsPerSecond;
        handle.DrawRect(new UIBox2(line - window, box.Top + 1f, line + window, box.Bottom - 1f), Gold.WithAlpha(dying.ReviveBeat ? 0.22f : 0.12f));
        handle.DrawLine(new Vector2(line, box.Top + 1f), new Vector2(line, box.Bottom - 1f), Gold);
        Marker(handle, new Vector2(line, box.Top + 1f), 1f);
        Marker(handle, new Vector2(line, box.Bottom - 1f), -1f);

        // the beats on the trace: the gone ones, the next one and the ones after it at the current pace
        _beats.Clear();
        foreach (var (at, color, _) in _past)
        {
            _beats.Add(((float) (at - now).TotalSeconds, color, false));
        }

        var count = dying.ReviveBeat ? 1 : 4;
        for (var i = 0; i < count; i++)
        {
            _beats.Add(((float) (dying.NextBeat - now).TotalSeconds + i * dying.BeatInterval, i == 0 ? ColorOf(dying.Result) : Trace, dying.ReviveBeat && i == 0));
        }

        // one continuous trace, the P wave, the QRS spike and the T wave of every beat
        var middle = box.Top + MonitorHeight * 0.6f;
        Vector2? previous = null;
        for (var x = box.Left + 4f; x <= box.Right - 4f; x += 1.5f)
        {
            var t = (x - line) / PixelsPerSecond;
            var value = 0f;
            var color = Trace;
            var nearest = float.MaxValue;
            foreach (var (at, beatColor, tall) in _beats)
            {
                var dt = t - at;
                value += Ecg(dt) * (tall ? 1.35f : 1f);
                if (MathF.Abs(dt) < nearest)
                {
                    nearest = MathF.Abs(dt);
                    color = beatColor;
                }
            }

            var point = new Vector2(x, middle - value * MonitorHeight * 0.42f);
            if (previous is { } from)
            {
                handle.DrawLine(from + new Vector2(0f, 1f), point + new Vector2(0f, 1f), color.WithAlpha(0.35f));
                handle.DrawLine(from, point, color);
            }

            previous = point;
        }

        // the frame lights up green or red for a moment after a press
        var since = (float) (_timing.RealTime - _feedbackAt).TotalSeconds;
        var flash = dying.Result != ST14BeatResult.None ? MathF.Max(0f, 1f - since / 0.6f) : 0f;
        var frame = dying.Result == ST14BeatResult.Hit ? Good : dying.Result == ST14BeatResult.Miss ? Bad : Trace;
        handle.DrawRect(box, Color.InterpolateBetween(Trace.WithAlpha(0.55f), frame, flash), false);
        if (flash > 0f)
            handle.DrawRect(new UIBox2(box.Left - 2f, box.Top - 2f, box.Right + 2f, box.Bottom + 2f), frame.WithAlpha(flash * 0.7f), false);

        _font ??= _cache.GetFont(FontPath, 13);
        _bold ??= _cache.GetFont(BoldPath, 16);
        var key = _input.TryGetKeyBinding(ST14KeyFunctions.Heartbeat, out var binding) ? binding.GetKeyString() : string.Empty;
        var text = dying.ReviveBeat
            ? Loc.GetString("st14-heartbeat-revive", ("key", key))
            : Loc.GetString("st14-heartbeat-hint", ("key", key));
        Centered(handle, _bold, text, new Vector2(box.Center.X, box.Top - 26f), dying.ReviveBeat ? Gold : Color.White);

        if (dying.Result != ST14BeatResult.None)
        {
            var verdict = Loc.GetString(dying.Result == ST14BeatResult.Hit ? "st14-heartbeat-hit" : "st14-heartbeat-miss");
            var size = handle.GetDimensions(_bold, verdict, 1f);
            handle.DrawString(_bold, new Vector2(box.Right - size.X - 10f, box.Top + 6f), verdict, frame.WithAlpha(0.5f + 0.5f * MathF.Max(flash, 0.4f)));
        }
    }

    /// <summary>
    /// The height of one heartbeat on the trace, dt seconds from its spike
    /// </summary>
    private static float Ecg(float dt)
    {
        return 0.13f * Gauss(dt + 0.17f, 0.028f)
               - 0.14f * Gauss(dt + 0.03f, 0.009f)
               + 1.00f * Gauss(dt, 0.010f)
               - 0.30f * Gauss(dt - 0.03f, 0.011f)
               + 0.24f * Gauss(dt - 0.24f, 0.045f);
    }

    private static float Gauss(float x, float width)
    {
        return MathF.Exp(-x * x / (2f * width * width));
    }

    private static void Marker(DrawingHandleScreen handle, Vector2 at, float down)
    {
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList,
            new[] { at + new Vector2(-6f, -6f * down), at + new Vector2(6f, -6f * down), at + new Vector2(0f, 3f * down) }, Gold);
    }

    private static void Centered(DrawingHandleScreen handle, Font font, string text, Vector2 at, Color color)
    {
        var size = handle.GetDimensions(font, text, 1f);
        var pos = at - new Vector2(size.X / 2f, 0f);
        handle.DrawRect(UIBox2.FromDimensions(pos - new Vector2(8f, 2f), size + new Vector2(16f, 4f)), Color.Black.WithAlpha(0.7f));
        handle.DrawString(font, pos, text, color);
    }

    private void DrawCharge(DrawingHandleScreen handle, Vector2 bottom, ST14FieldDefibComponent defib)
    {
        var box = UIBox2.FromDimensions(bottom.X - BarWidth / 2f, bottom.Y - BarHeight, BarWidth, BarHeight);
        handle.DrawRect(box, Back);

        var green = new UIBox2(box.Left + BarWidth * defib.GreenStart, box.Top, box.Left + BarWidth * defib.GreenEnd, box.Bottom);
        var hot = new UIBox2(green.Right, box.Top, box.Right, box.Bottom);
        handle.DrawRect(green, Good.WithAlpha(0.7f));
        handle.DrawRect(hot, Hot.WithAlpha(0.7f));
        handle.DrawRect(box, Gold, false);

        var x = box.Left + BarWidth * Math.Clamp(_defib.Charge(defib), 0f, 1f);
        handle.DrawLine(new Vector2(x, box.Top - 3f), new Vector2(x, box.Bottom + 3f), Color.White);

        _font ??= _cache.GetFont(FontPath, 13);
        handle.DrawString(_font, new Vector2(box.Left, box.Top - 22f), Loc.GetString("st14-field-defib-hint"), Color.White);
    }
}
