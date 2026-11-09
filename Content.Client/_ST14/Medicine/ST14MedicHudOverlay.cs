using System.Linq;
using System.Numerics;
using Content.Shared._ST14.Medicine;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// What a medic sees over the wounded: a pulsing red cross over whoever calls for a medic, a black heart over a
/// stopped one, a blood drop growing with the bleeding, a strap for every tourniquet turning red when the limb is
/// dying, a bone for a fracture without a splint, and the blood left as a thin bar
/// </summary>
public sealed partial class ST14MedicHudOverlay : Overlay
{
    private const float Icon = 0.16f;

    private static readonly Color Blood = Color.FromHex("#D11F1F");
    private static readonly Color Strap = Color.FromHex("#E0B341");
    private static readonly Color Dying = Color.FromHex("#FF4A2E");
    private static readonly Color Bone = Color.FromHex("#F2EEDF");
    private static readonly Color Outline = Color.FromHex("#000000C0");

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14MedicHudOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 5;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entities.HasComponent<ST14MedicHudComponent>(_player.LocalEntity);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var transform = _entities.System<SharedTransformSystem>();
        var mobState = _entities.System<MobStateSystem>();
        var time = (float) _timing.RealTime.TotalSeconds;
        var pulse = 0.5f + 0.5f * MathF.Sin(time * 6f);
        var bounds = args.WorldAABB.Enlarged(1f);

        var query = _entities.EntityQueryEnumerator<ST14WoundsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wounds, out var xform))
        {
            if (uid == _player.LocalEntity || xform.MapID != args.MapId)
                continue;

            var position = transform.GetWorldPosition(xform);
            if (!bounds.Contains(position))
                continue;

            var dead = mobState.IsDead(uid);
            _entities.TryGetComponent<ST14CallingMedicComponent>(uid, out var call);
            var calling = call != null;
            var stopped = wounds.Heart == ST14HeartState.Arrest && !dead;
            var fracture = wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Fracture && !w.Splinted);
            var dying = wounds.Tourniquets.Values.Any(s => s >= wounds.IschemiaTime);

            var icons = new List<Action<Vector2>>();
            if (stopped)
                icons.Add(at => DrawHeart(handle, at, pulse));

            if (wounds.Bleeding > 0.05f && !dead)
                icons.Add(at => DrawDrop(handle, at, wounds.Bleeding, pulse));

            if (wounds.Tourniquets.Count > 0)
                icons.Add(at => DrawStrap(handle, at, dying ? Color.InterpolateBetween(Strap, Dying, pulse) : Strap));

            if (fracture)
                icons.Add(at => DrawBone(handle, at));

            if (wounds.Infection >= 0.3f && !dead)
                icons.Add(at => DrawInfection(handle, at, wounds.Infection >= 0.8f ? pulse : 0f));

            if (wounds.Toxin >= 0.15f && !dead)
                icons.Add(at => DrawVenom(handle, at));

            var top = position + new Vector2(0f, 0.75f);
            if (calling && !dead)
            {
                var crossColor = call!.Responder == null ? Color.FromHex("#E01818")
                    : call.Responder == _player.LocalEntity ? Color.FromHex("#2FBF5A") : Color.FromHex("#8A8A8A");
                DrawCross(handle, top + new Vector2(0f, 0.32f), 0.2f + (call.Responder == null ? 0.04f * pulse : 0f), crossColor);
                top += new Vector2(0f, 0.02f);
            }

            var width = icons.Count * Icon * 2.2f;
            var left = top - new Vector2(width / 2f - Icon * 1.1f, 0f);
            for (var i = 0; i < icons.Count; i++)
            {
                icons[i](left + new Vector2(i * Icon * 2.2f, 0f));
            }

            // the blood left: a thin bar under the icons once the body has lost some
            if (!dead && wounds.Blood < 0.95f)
            {
                var bar = new Box2(top.X - 0.3f, top.Y - 0.22f, top.X + 0.3f, top.Y - 0.18f);
                handle.DrawRect(bar.Enlarged(0.015f), Outline);
                handle.DrawRect(new Box2(bar.Left, bar.Bottom, bar.Left + bar.Width * Math.Clamp(wounds.Blood, 0f, 1f), bar.Top),
                    wounds.Blood < 0.6f ? Color.InterpolateBetween(Blood, Color.White, pulse * 0.4f) : Blood);
            }
        }
    }

    private static void Triangle(DrawingHandleWorld handle, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[] { a, b, c }, color);
    }

    private static void DrawDrop(DrawingHandleWorld handle, Vector2 at, float bleeding, float pulse)
    {
        var size = Icon * (0.75f + MathF.Min(0.6f, bleeding * 0.25f)) * (bleeding >= 1f ? 1f + 0.15f * pulse : 1f);
        handle.DrawCircle(at - new Vector2(0f, size * 0.25f), size * 0.62f, Outline);
        Triangle(handle, at + new Vector2(0f, size * 1.05f), at - new Vector2(size * 0.68f, size * 0.15f), at + new Vector2(size * 0.68f, -size * 0.15f), Outline);
        handle.DrawCircle(at - new Vector2(0f, size * 0.25f), size * 0.5f, Blood);
        Triangle(handle, at + new Vector2(0f, size * 0.9f), at - new Vector2(size * 0.52f, size * 0.12f), at + new Vector2(size * 0.52f, -size * 0.12f), Blood);
    }

    private static void DrawStrap(DrawingHandleWorld handle, Vector2 at, Color color)
    {
        var box = new Box2(at.X - Icon * 0.9f, at.Y - Icon * 0.32f, at.X + Icon * 0.9f, at.Y + Icon * 0.32f);
        handle.DrawRect(box.Enlarged(0.02f), Outline);
        handle.DrawRect(box, color);
        handle.DrawRect(new Box2(at.X + Icon * 0.35f, box.Bottom, at.X + Icon * 0.6f, box.Top), Outline);
    }

    private static void DrawBone(DrawingHandleWorld handle, Vector2 at)
    {
        var from = at + new Vector2(-Icon * 0.7f, -Icon * 0.5f);
        var to = at + new Vector2(Icon * 0.7f, Icon * 0.5f);
        foreach (var end in new[] { from, to })
        {
            handle.DrawCircle(end, Icon * 0.32f, Outline);
        }

        handle.DrawLine(from, to, Outline);
        foreach (var end in new[] { from, to })
        {
            handle.DrawCircle(end, Icon * 0.24f, Bone);
        }

        var dir = Vector2.Normalize(to - from);
        var side = new Vector2(-dir.Y, dir.X) * Icon * 0.12f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, new[] { from + side, from - side, to + side, to - side }, Bone);
    }

    private static void DrawInfection(DrawingHandleWorld handle, Vector2 at, float pulse)
    {
        var colour = Color.InterpolateBetween(Color.FromHex("#7FBF2E"), Color.FromHex("#D8FF60"), pulse);
        handle.DrawCircle(at, Icon * 0.62f, Outline);
        handle.DrawCircle(at, Icon * 0.5f, colour);
        foreach (var dot in new[] { new Vector2(-0.2f, 0.15f), new Vector2(0.2f, 0.12f), new Vector2(0f, -0.22f) })
        {
            handle.DrawCircle(at + dot * Icon, Icon * 0.12f, Color.FromHex("#3E5E12"));
        }
    }

    private static void DrawVenom(DrawingHandleWorld handle, Vector2 at)
    {
        var size = Icon * 0.85f;
        var colour = Color.FromHex("#8BD12E");
        handle.DrawCircle(at - new Vector2(0f, size * 0.25f), size * 0.62f, Outline);
        Triangle(handle, at + new Vector2(0f, size * 1.05f), at - new Vector2(size * 0.68f, size * 0.15f), at + new Vector2(size * 0.68f, -size * 0.15f), Outline);
        handle.DrawCircle(at - new Vector2(0f, size * 0.25f), size * 0.5f, colour);
        Triangle(handle, at + new Vector2(0f, size * 0.9f), at - new Vector2(size * 0.52f, size * 0.12f), at + new Vector2(size * 0.52f, -size * 0.12f), colour);
    }

    private static void DrawHeart(DrawingHandleWorld handle, Vector2 at, float pulse)
    {
        var color = Color.InterpolateBetween(Color.FromHex("#1A1A1A"), Color.FromHex("#E01010"), pulse);
        var r = Icon * 0.45f;
        handle.DrawCircle(at + new Vector2(-r * 0.75f, r * 0.35f), r * 1.15f, Outline);
        handle.DrawCircle(at + new Vector2(r * 0.75f, r * 0.35f), r * 1.15f, Outline);
        handle.DrawCircle(at + new Vector2(-r * 0.75f, r * 0.35f), r, color);
        handle.DrawCircle(at + new Vector2(r * 0.75f, r * 0.35f), r, color);
        Triangle(handle, at + new Vector2(-r * 1.7f, r * 0.2f), at + new Vector2(r * 1.7f, r * 0.2f), at - new Vector2(0f, r * 1.9f), color);
    }

    private static void DrawCross(DrawingHandleWorld handle, Vector2 at, float size, Color fill)
    {
        var arm = size * 0.33f;
        foreach (var (box, color) in new[] { (0.035f, Color.White), (0f, fill) })
        {
            handle.DrawRect(new Box2(at.X - arm - box, at.Y - size - box, at.X + arm + box, at.Y + size + box), color);
            handle.DrawRect(new Box2(at.X - size - box, at.Y - arm - box, at.X + size + box, at.Y + arm + box), color);
        }
    }
}
