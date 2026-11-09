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
    private const float MonitorWidth = 420f;
    private const float MonitorHeight = 84f;
    private const float PixelsPerSecond = 170f;
    private const float PressLine = 0.25f;
    private const float BarWidth = 320f;
    private const float BarHeight = 22f;
    private const float BottomMargin = 150f;

    private static readonly Color Back = Color.Black.WithAlpha(0.75f);
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
        handle.DrawRect(box, Back);
        handle.DrawRect(box, Trace.WithAlpha(0.5f), false);

        var color = dying.Result switch
        {
            ST14BeatResult.Hit => Good,
            ST14BeatResult.Miss => Bad,
            _ => Trace,
        };

        var middle = box.Top + MonitorHeight * 0.55f;
        var line = box.Left + MonitorWidth * PressLine;
        handle.DrawLine(new Vector2(line, box.Top + 4f), new Vector2(line, box.Bottom - 4f), Gold);
        handle.DrawLine(new Vector2(box.Left, middle), new Vector2(box.Right, middle), color.WithAlpha(0.5f));

        // the next beat, then the ones after it at the current pace. The one after a shock stands alone and tall
        var now = _timing.CurTime;
        var height = dying.ReviveBeat ? 34f : 24f;
        var count = dying.ReviveBeat ? 1 : 4;
        for (var i = -1; i < count; i++)
        {
            var at = (float) (dying.NextBeat - now).TotalSeconds + i * dying.BeatInterval;
            DrawSpike(handle, line + at * PixelsPerSecond, middle, height, color, box);
        }

        _font ??= _cache.GetFont(FontPath, 13);
        var key = _input.TryGetKeyBinding(ST14KeyFunctions.Heartbeat, out var binding) ? binding.GetKeyString() : string.Empty;
        var text = dying.ReviveBeat
            ? Loc.GetString("st14-heartbeat-revive", ("key", key))
            : Loc.GetString("st14-heartbeat-hint", ("key", key));
        handle.DrawString(_font, new Vector2(box.Left + 8f, box.Top + 2f), text, dying.ReviveBeat ? Gold : Color.White);
    }

    private static void DrawSpike(DrawingHandleScreen handle, float x, float y, float height, Color color, UIBox2 box)
    {
        if (x < box.Left + 8f || x > box.Right - 8f)
            return;

        var low = new Vector2(x - 8f, y);
        var up = new Vector2(x - 2f, y - height);
        var down = new Vector2(x + 3f, y + height * 0.4f);
        var back = new Vector2(x + 8f, y);
        handle.DrawLine(low, up, color);
        handle.DrawLine(up, down, color);
        handle.DrawLine(down, back, color);
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
