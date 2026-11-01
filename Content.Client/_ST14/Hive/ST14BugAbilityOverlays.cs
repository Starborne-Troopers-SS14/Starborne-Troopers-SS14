using System.Numerics;
using Content.Client.Resources;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Green motes circling warriors at full resonance, the swarm humming as one
/// </summary>
public sealed partial class ST14ResonanceOverlay : Overlay
{
    private const int Motes = 10;

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;
    private readonly MobStateSystem _mobState;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14ResonanceOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        _mobState = _entities.System<MobStateSystem>();
        ZIndex = 5;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;

        var query = _entities.EntityQueryEnumerator<ST14SwarmResonanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Allies < comp.MaxAllies || xform.MapID != args.MapId || _mobState.IsDead(uid))
                continue;

            var position = _transform.GetWorldPosition(xform);
            var seed = uid.Id * 0.731f;

            for (var i = 0; i < Motes; i++)
            {
                // each mote circles the bug on a flat ellipse at its own speed and bobs up and down
                var speed = 0.8f + (i % 3) * 0.25f;
                var angle = time * speed + i * MathF.Tau / Motes + seed;
                var radius = 0.5f + 0.1f * MathF.Sin(time * 1.3f + i);
                var bob = 0.12f * MathF.Sin(time * 2.1f + i * 1.7f);
                var mote = position + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * 0.45f + 0.1f + bob);

                // motes behind the bug are dimmer, so the ring reads as going around it
                var front = 0.55f + 0.45f * -MathF.Sin(angle);
                var twinkle = 0.6f + 0.4f * MathF.Sin(time * 5f + i * 2.3f);
                var size = 0.045f + 0.02f * twinkle;

                handle.DrawRect(Box2.CenteredAround(mote, new Vector2(size * 2.2f)), new Color(0.4f, 1f, 0.3f, 0.12f * front * twinkle));
                handle.DrawRect(Box2.CenteredAround(mote, new Vector2(size)), new Color(0.7f, 1f, 0.55f, 0.85f * front * twinkle));
            }
        }
    }
}

/// <summary>
/// Red sparks of rage rising off a tiger in its last rush
/// </summary>
public sealed partial class ST14RageOverlay : Overlay
{
    private const int Sparks = 18;
    private const float SparkLife = 0.9f;

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14RageOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        ZIndex = 5;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;

        var query = _entities.EntityQueryEnumerator<ST14LastRushComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var rush, out var xform))
        {
            if (!rush.Active || xform.MapID != args.MapId)
                continue;

            var position = _transform.GetWorldPosition(xform);
            var seed = uid.Id * 0.137f;

            for (var i = 0; i < Sparks; i++)
            {
                // every spark loops on its own phase: born around the body, drifts up and sways, fades out
                var phase = (time / SparkLife + i * 0.618f + seed) % 1f;
                var angle = Hash(i, seed) * MathF.Tau;
                var radius = 0.25f + Hash(i + 31, seed) * 0.35f;
                var start = position + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * 0.6f - 0.1f);
                var drift = new Vector2(MathF.Sin(time * 3f + i) * 0.08f, phase * 0.9f);

                var size = 0.035f + Hash(i + 7, seed) * 0.04f;
                var alpha = MathF.Sin(phase * MathF.PI) * 0.85f;
                var color = Color.InterpolateBetween(new Color(1f, 0.55f, 0.3f), new Color(0.85f, 0.05f, 0.05f), phase);

                var center = start + drift;
                handle.DrawRect(Box2.CenteredAround(center, new Vector2(size)), color.WithAlpha(alpha));
            }
        }
    }

    private static float Hash(int i, float seed)
    {
        var v = MathF.Sin(i * 12.9898f + seed * 78.233f) * 43758.547f;
        return v - MathF.Floor(v);
    }
}

/// <summary>
/// Trails of blood drops left by the badly wounded troopers, seen by the bugs that smell blood. Drawn over everything,
/// walls included, and unlit so they stand out in the dark
/// </summary>
public sealed partial class ST14BloodScentOverlay : Overlay
{
    private const float Lifetime = 20f;

    // Drops between two trail points, so the trail reads as one line of blood
    private const float DropSpacing = 0.3f;

    // The drop sprite is 4x8 px
    private static readonly Vector2 DropSize = new(4f / 32f, 8f / 32f);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resources = default!;

    private Texture? _drop;
    private List<ST14BloodTrail> _trails = new();
    private TimeSpan _received;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14BloodScentOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 6;
    }

    /// <summary>
    /// Troopers of the trails, while they are fresh
    /// </summary>
    public IEnumerable<NetEntity> Troopers()
    {
        if (!Fresh())
            yield break;

        foreach (var trail in _trails)
        {
            yield return trail.Trooper;
        }
    }

    public void SetTrails(List<ST14BloodTrail> trails, TimeSpan received)
    {
        _trails = trails;
        _received = received;
    }

    // the trails are only fresh for a moment after the last update, a bug that stopped smelling stops seeing them
    private bool Fresh()
    {
        return _trails.Count > 0 && (_timing.RealTime - _received).TotalSeconds < 2;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Fresh();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _drop ??= _resources.GetTexture("/Textures/_ST14/Effects/BloodScent/drop.png");

        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;
        var since = (float) (_timing.RealTime - _received).TotalSeconds;
        var glow = 0.8f + 0.2f * MathF.Sin(time * 4f);

        foreach (var trail in _trails)
        {
            if (trail.Map != args.MapId)
                continue;

            for (var i = 0; i < trail.Points.Count; i++)
            {
                var fade = Math.Clamp(1f - (trail.Ages[i] + since) / Lifetime, 0f, 1f);
                if (fade <= 0f)
                    continue;

                // drops point the way the trooper went: from this point to the next, the newest one from the one before
                var point = trail.Points[i];
                var heading = i + 1 < trail.Points.Count ? trail.Points[i + 1] - point
                    : i > 0 ? point - trail.Points[i - 1]
                    : new Vector2(0f, 1f);
                DrawDrop(handle, point, heading, i * 3, fade * glow);

                if (i == 0)
                    continue;

                // fill the gap to the previous point with drops that fade with it
                var previous = trail.Points[i - 1];
                var gap = point - previous;
                var steps = (int) (gap.Length() / DropSpacing);
                var previousFade = Math.Clamp(1f - (trail.Ages[i - 1] + since) / Lifetime, 0f, 1f);
                for (var s = 1; s < steps; s++)
                {
                    var t = s / (float) steps;
                    DrawDrop(handle, previous + gap * t, gap, i * 3 + s, MathHelper.Lerp(previousFade, fade, t) * glow);
                }
            }
        }
    }

    private void DrawDrop(DrawingHandleWorld handle, Vector2 position, Vector2 heading, int index, float alpha)
    {
        // drops scatter a little around the path, like blood dripping off a running man
        var jitter = new Vector2(Hash(index) - 0.5f, Hash(index + 17) - 0.5f) * 0.18f;
        var box = Box2.CenteredAround(position + jitter, DropSize);

        // the sprite's round head is at its top: turn the top towards where the trooper went
        var angle = heading.LengthSquared() > 0.0001f
            ? new Angle(heading) - Angle.FromDegrees(90)
            : Angle.Zero;

        handle.DrawTextureRect(_drop!, new Box2Rotated(box, angle, box.Center), Color.White.WithAlpha(alpha));
    }

    private static float Hash(int i)
    {
        var v = MathF.Sin(i * 12.9898f) * 43758.547f;
        return v - MathF.Floor(v);
    }
}

/// <summary>
/// The struggle of a trooper in the claws, on its own screen: a round key to press with a ring closing in on it and how
/// close the trooper is to breaking free. The key flashes green on a perfect press
/// </summary>
public sealed partial class ST14GripStruggleOverlay : Overlay
{
    private const float KeyRadius = 30f;
    private static readonly TimeSpan PerfectFlash = TimeSpan.FromSeconds(0.35);

    private static readonly Color KeyFill = new(0.1f, 0.07f, 0.05f, 0.9f);
    private static readonly Color Edge = new(0.9f, 0.7f, 0.35f);
    private static readonly Color Perfect = new(0.35f, 0.95f, 0.3f);
    private static readonly Color Danger = new(0.95f, 0.15f, 0.1f);

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;

    private Font? _keyFont;
    private Font? _textFont;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public ST14GripStruggleOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entities.HasComponent<ST14HiveGrabbedComponent>(_player.LocalEntity);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entities.TryGetComponent(_player.LocalEntity, out ST14HiveGrabbedComponent? grabbed))
            return;

        _keyFont ??= _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 26);
        _textFont ??= _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);

        var handle = args.ScreenHandle;
        var center = args.ViewportBounds.Center + new Vector2(0f, args.ViewportBounds.Height * 0.22f);
        var time = (float) _timing.RealTime.TotalSeconds;
        var now = _timing.CurTime;

        // the ring shrinks onto the key, the perfect moment is when it touches the key's edge
        var lead = (float) grabbed.PromptLead.TotalSeconds;
        var left = (float) (grabbed.PromptPerfect - now).TotalSeconds;
        var onTime = MathF.Abs(left) <= grabbed.PerfectWindow.TotalSeconds;
        var flash = now - grabbed.LastPerfect < PerfectFlash;
        var ringRadius = KeyRadius * (1.1f + 2f * Math.Clamp(left / lead, 0f, 1f));

        var shake = new Vector2(MathF.Sin(time * 40f), MathF.Cos(time * 33f)) * 1.5f;
        DrawCentered(handle, _textFont, Loc.GetString("st14-bug-grab-ui-title"), center + new Vector2(0f, -78f) + shake, Danger, 1.4f, true);

        // the round key: green while it is the perfect moment and right after a perfect press
        var green = flash || onTime;
        handle.DrawCircle(center, KeyRadius + 3f, Color.Black.WithAlpha(0.6f));
        handle.DrawCircle(center, KeyRadius, flash ? Perfect : KeyFill);
        handle.DrawCircle(center, KeyRadius, green ? Perfect : Edge, false);
        handle.DrawCircle(center, KeyRadius - 1f, green ? Perfect : Edge, false);

        if (!flash && left > -grabbed.PerfectWindow.TotalSeconds)
        {
            var ring = green ? Perfect : Color.White.WithAlpha(0.9f);
            handle.DrawCircle(center, ringRadius, ring, false);
            handle.DrawCircle(center, ringRadius + 1f, ring.WithAlpha(0.5f), false);
        }

        DrawCentered(handle, _keyFont, KeyName(grabbed.Prompt), center, flash ? Color.Black : green ? Perfect : Color.White, 1f, !flash);

        // how close the trooper is to breaking free
        var progress = Math.Clamp(grabbed.Struggle / grabbed.StruggleNeeded, 0f, 1f);
        var bar = new UIBox2(center + new Vector2(-110f, 58f), center + new Vector2(110f, 68f));
        handle.DrawRect(bar, Color.Black.WithAlpha(0.6f));
        handle.DrawRect(new UIBox2(bar.TopLeft, new Vector2(bar.Left + bar.Width * progress, bar.Bottom)),
            Color.InterpolateBetween(Danger, Perfect, progress));
        handle.DrawRect(bar, Edge, false);

        DrawCentered(handle, _textFont, Loc.GetString("st14-bug-grab-ui-hint"), center + new Vector2(0f, 86f), Color.White.WithAlpha(0.85f), 1f, true);
    }

    private static void DrawCentered(DrawingHandleScreen handle, Font font, string text, Vector2 center, Color color, float scale, bool shadow)
    {
        var size = handle.GetDimensions(font, text, scale);
        var pos = center - size / 2f;

        // no backdrop behind the prompt, a dark shadow keeps the text readable on any floor
        if (shadow)
            handle.DrawString(font, pos + new Vector2(2f, 2f), text, scale, Color.Black.WithAlpha(0.8f));

        handle.DrawString(font, pos, text, scale, color);
    }

    /// <summary>
    /// The key the player has bound to the movement direction, so the prompt shows what to actually press
    /// </summary>
    private string KeyName(MoveButtons button)
    {
        var (function, fallback) = button switch
        {
            MoveButtons.Up => (EngineKeyFunctions.MoveUp, "W"),
            MoveButtons.Down => (EngineKeyFunctions.MoveDown, "S"),
            MoveButtons.Left => (EngineKeyFunctions.MoveLeft, "A"),
            _ => (EngineKeyFunctions.MoveRight, "D"),
        };

        return _input.TryGetKeyBinding(function, out var binding) ? binding.GetKeyString() : fallback;
    }
}
