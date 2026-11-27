using System.Numerics;
using Content.Client.Resources;
using Content.Shared._ST14.Medicine;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// The badly wounded as the server tells them: a medic sees every one of them, near ones with their distance under the
/// cross, far ones as an arrow on the edge of the screen
/// </summary>
public sealed partial class ST14MedicCallsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public List<ST14MedicCall> Calls { get; private set; } = new();

    private ST14MedicCallsOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14MedicCallsEvent>(ev => Calls = ev.Calls);

        _overlay = new ST14MedicCallsOverlay(this);
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }
}

public sealed partial class ST14MedicCallsOverlay : Overlay
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const float Edge = 46f;

    private static readonly Color Open = Color.FromHex("#FF4040");
    private static readonly Color Shadow = Color.Black.WithAlpha(0.8f);

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _cache = default!;

    private readonly ST14MedicCallsSystem _system;
    private Font? _font;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public ST14MedicCallsOverlay(ST14MedicCallsSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        ZIndex = 6;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } local)
            return;

        _font ??= _cache.GetFont(FontPath, 12);
        var handle = args.ScreenHandle;
        var bounds = args.ViewportBounds;
        _screen = new UIBox2(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

        if (!_entities.HasComponent<ST14MedicHudComponent>(local))
            return;

        var xform = _entities.GetComponent<TransformComponent>(local);
        var transform = _entities.System<SharedTransformSystem>();
        var here = transform.GetMapCoordinates(local, xform);
        foreach (var call in _system.Calls)
        {
            var caller = _entities.GetEntity(call.Caller);
            if (caller == local || call.Map != (int) here.MapId)
                continue;

            var distance = (int) MathF.Round((call.Position - here.Position).Length());
            var color = Open;
            var label = Loc.GetString("st14-medic-mark", ("name", call.Name), ("distance", distance));

            var screen = _eye.MapToScreen(new MapCoordinates(call.Position, here.MapId)).Position;
            var inner = new UIBox2(bounds.Left + Edge, bounds.Top + Edge, bounds.Right - Edge, bounds.Bottom - Edge);
            if (inner.Contains(screen))
            {
                // on the screen: the world cross is over the head, the distance goes under the feet
                DrawCentered(handle, _font, label, screen + new Vector2(0f, 26f), color);
                continue;
            }

            // off the screen: an arrow on the edge pointing at the caller, with the cross and the distance
            var center = new Vector2((bounds.Left + bounds.Right) / 2f, (bounds.Top + bounds.Bottom) / 2f);
            var direction = screen - center;
            if (direction.LengthSquared() < 1f)
                continue;

            direction = Vector2.Normalize(direction);
            var scale = MathF.Min(
                MathF.Abs(direction.X) > 0.0001f ? (inner.Width / 2f) / MathF.Abs(direction.X) : float.MaxValue,
                MathF.Abs(direction.Y) > 0.0001f ? (inner.Height / 2f) / MathF.Abs(direction.Y) : float.MaxValue);
            var at = center + direction * scale;
            DrawArrow(handle, at, direction, color);
            DrawCross(handle, at - direction * 26f, color);
            DrawCentered(handle, _font, label, at - direction * 26f + new Vector2(0f, 20f), color);
        }
    }

    private UIBox2 _screen;

    private void DrawCentered(DrawingHandleScreen handle, Font font, string text, Vector2 at, Color color)
    {
        var size = handle.GetDimensions(font, text, 1f);
        var pos = at - new Vector2(size.X / 2f, 0f);

        // a label by the edge of the screen stays whole on it
        pos.X = Math.Clamp(pos.X, _screen.Left + 8f, MathF.Max(_screen.Left + 8f, _screen.Right - size.X - 8f));
        pos.Y = Math.Clamp(pos.Y, _screen.Top + 4f, MathF.Max(_screen.Top + 4f, _screen.Bottom - size.Y - 4f));
        handle.DrawRect(UIBox2.FromDimensions(pos - new Vector2(6f, 2f), size + new Vector2(12f, 4f)), Shadow);
        handle.DrawString(font, pos, text, color);
    }

    private static void DrawCross(DrawingHandleScreen handle, Vector2 at, Color color)
    {
        const float size = 10f;
        const float arm = 3.5f;
        foreach (var (pad, fill) in new[] { (2f, Color.White), (0f, color) })
        {
            handle.DrawRect(new UIBox2(at.X - arm - pad, at.Y - size - pad, at.X + arm + pad, at.Y + size + pad), fill);
            handle.DrawRect(new UIBox2(at.X - size - pad, at.Y - arm - pad, at.X + size + pad, at.Y + arm + pad), fill);
        }
    }

    private static void DrawArrow(DrawingHandleScreen handle, Vector2 tip, Vector2 direction, Color color)
    {
        var side = new Vector2(-direction.Y, direction.X);
        var back = tip - direction * 16f;
        var a = back + side * 9f;
        var b = back - side * 9f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[] { tip, a, b }, color);
    }
}
