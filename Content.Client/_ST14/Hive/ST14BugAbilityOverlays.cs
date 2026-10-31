using System.Numerics;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Bends the air around warriors at full resonance (green) and tigers in their last rush (red)
/// </summary>
public sealed partial class ST14HiveShimmerOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShimmerShader = "ST14HiveShimmer";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;

    private readonly SharedTransformSystem _transform;
    private readonly MobStateSystem _mobState;

    // made on the first draw: a client without graphics, like the test one, never needs them
    private Texture? _circle;
    private ShaderInstance? _resonance;
    private ShaderInstance? _rage;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public ST14HiveShimmerOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        _mobState = _entities.System<MobStateSystem>();
        ZIndex = 5;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _circle ??= _resources.GetTexture("/Textures/Effects/HeatBlur/soft_circle.png");
        _resonance ??= MakeShader(new Color(0.55f, 0.95f, 0.35f), 5f, 0.12f);
        _rage ??= MakeShader(new Color(1f, 0.15f, 0.1f), 8f, 0.25f);

        var handle = args.WorldHandle;

        _resonance.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_resonance);
        var resonance = _entities.EntityQueryEnumerator<ST14SwarmResonanceComponent, TransformComponent>();
        while (resonance.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Allies >= comp.MaxAllies && xform.MapID == args.MapId && !_mobState.IsDead(uid))
                DrawAround(handle, args.WorldAABB, _transform.GetWorldPosition(xform), 1.1f);
        }

        _rage.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        handle.UseShader(_rage);
        var rush = _entities.EntityQueryEnumerator<ST14LastRushComponent, TransformComponent>();
        while (rush.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Active && xform.MapID == args.MapId && !_mobState.IsDead(uid))
                DrawAround(handle, args.WorldAABB, _transform.GetWorldPosition(xform), 1.4f);
        }

        handle.UseShader(null);
    }

    private void DrawAround(DrawingHandleWorld handle, Box2 view, Vector2 position, float radius)
    {
        var box = Box2.CenteredAround(position, new Vector2(radius * 2f));
        if (view.Intersects(box))
            handle.DrawTextureRect(_circle!, box);
    }

    private ShaderInstance MakeShader(Color tint, float strength, float tintStrength)
    {
        var shader = _prototypes.Index(ShimmerShader).InstanceUnique();
        shader.SetParameter("tint", new Vector3(tint.R, tint.G, tint.B));
        shader.SetParameter("strength", strength);
        shader.SetParameter("tint_strength", tintStrength);
        return shader;
    }
}

/// <summary>
/// Draws the sinews that lock the bugs of a living barricade together and the faint plates of its shield
/// </summary>
public sealed partial class ST14BarricadeOverlay : Overlay
{
    private static readonly Color Sinew = new(0.55f, 0.75f, 0.25f);
    private static readonly Color SinewCore = new(0.85f, 1f, 0.5f);

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;
    private readonly HashSet<EntityUid> _drawn = new();
    private readonly List<Vector2> _vertices = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14BarricadeOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        ZIndex = 4;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;
        var pulse = 0.5f + 0.5f * MathF.Sin(time * 4f);

        _drawn.Clear();
        var query = _entities.EntityQueryEnumerator<ST14InBarricadeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wall, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var position = _transform.GetWorldPosition(xform);

            // the shield plate around every bug of the wall
            handle.DrawCircle(position, 0.75f, Sinew.WithAlpha(0.08f + 0.06f * pulse));
            handle.DrawCircle(position, 0.75f, Sinew.WithAlpha(0.35f), false);

            // a sinew from this bug to every next one in the wall, each pair drawn once
            _drawn.Add(uid);
            foreach (var other in wall.Members)
            {
                if (_drawn.Contains(other) ||
                    !_entities.TryGetComponent(other, out TransformComponent? otherXform) ||
                    otherXform.MapID != args.MapId)
                    continue;

                var to = _transform.GetWorldPosition(otherXform);
                DrawBand(handle, position, to, 0.16f, Sinew.WithAlpha(0.55f + 0.2f * pulse));
                DrawBand(handle, position, to, 0.05f, SinewCore.WithAlpha(0.6f + 0.3f * pulse));
            }
        }
    }

    private void DrawBand(DrawingHandleWorld handle, Vector2 from, Vector2 to, float width, Color color)
    {
        var along = to - from;
        if (along.LengthSquared() < 0.0001f)
            return;

        var side = new Vector2(-along.Y, along.X) / along.Length() * (width / 2f);

        _vertices.Clear();
        _vertices.Add(from + side);
        _vertices.Add(from - side);
        _vertices.Add(to + side);
        _vertices.Add(from - side);
        _vertices.Add(to - side);
        _vertices.Add(to + side);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _vertices.ToArray(), color);
    }
}

/// <summary>
/// Red trails of the badly wounded troopers, seen by the bugs that smell blood. Drawn over everything, walls included
/// </summary>
public sealed partial class ST14BloodScentOverlay : Overlay
{
    private const float Lifetime = 20f;
    private static readonly Color Blood = new(0.85f, 0.05f, 0.05f);
    private static readonly Color BloodLight = new(1f, 0.35f, 0.3f);

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SharedTransformSystem _transform;
    private List<ST14BloodTrail> _trails = new();
    private TimeSpan _received;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14BloodScentOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entities.System<SharedTransformSystem>();
        ZIndex = 6;
    }

    public void SetTrails(List<ST14BloodTrail> trails, TimeSpan received)
    {
        _trails = trails;
        _received = received;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // the trails are only fresh for a moment after the last update, a bug that stopped smelling stops seeing them
        return _trails.Count > 0 && (_timing.RealTime - _received).TotalSeconds < 2;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;
        var since = (float) (_timing.RealTime - _received).TotalSeconds;

        foreach (var trail in _trails)
        {
            if (trail.Map != args.MapId)
                continue;

            for (var i = 0; i < trail.Points.Count; i++)
            {
                var fade = Math.Clamp(1f - (trail.Ages[i] + since) / Lifetime, 0f, 1f);
                if (fade <= 0f)
                    continue;

                var point = trail.Points[i];
                handle.DrawCircle(point, 0.32f, Blood.WithAlpha(0.12f * fade));
                handle.DrawCircle(point, 0.12f, Blood.WithAlpha(0.75f * fade));

                if (i > 0)
                    handle.DrawLine(trail.Points[i - 1], point, Blood.WithAlpha(0.45f * fade));
            }

            // the wounded trooper itself, where the scent is strongest
            var head = trail.Points.Count > 0 ? trail.Points[^1] : Vector2.Zero;
            if (_entities.TryGetEntity(trail.Trooper, out var trooper) &&
                _entities.TryGetComponent(trooper, out TransformComponent? xform) &&
                xform.MapID == args.MapId)
            {
                head = _transform.GetWorldPosition(xform);
            }
            else if (trail.Points.Count == 0)
            {
                continue;
            }

            var beat = 0.5f + 0.5f * MathF.Sin(time * 6f);
            handle.DrawCircle(head, 0.55f + 0.15f * beat, Blood.WithAlpha(0.18f + 0.12f * beat));
            handle.DrawCircle(head, 0.7f + 0.2f * beat, BloodLight.WithAlpha(0.5f), false);
        }
    }
}

/// <summary>
/// The struggle of a trooper in the claws, on its own screen: the key to press with a ring closing in on it and how
/// close the trooper is to breaking free
/// </summary>
public sealed partial class ST14GripStruggleOverlay : Overlay
{
    private const float KeySize = 64f;

    private static readonly Color Panel = new(0.08f, 0.06f, 0.05f, 0.85f);
    private static readonly Color Edge = new(0.85f, 0.65f, 0.3f);
    private static readonly Color Perfect = new(0.45f, 1f, 0.4f);
    private static readonly Color Danger = new(0.9f, 0.15f, 0.1f);

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

        _keyFont ??= _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 28);
        _textFont ??= _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);

        var handle = args.ScreenHandle;
        var center = args.ViewportBounds.Center + new Vector2(0f, args.ViewportBounds.Height * 0.22f);
        var time = (float) _timing.RealTime.TotalSeconds;

        // the ring shrinks from three key sizes down to the key itself, the perfect moment is when it touches the key
        var lead = (float) grabbed.PromptLead.TotalSeconds;
        var left = (float) (grabbed.PromptPerfect - _timing.CurTime).TotalSeconds;
        var onTime = MathF.Abs(left) <= grabbed.PerfectWindow.TotalSeconds;
        var ringRadius = KeySize / 2f * (1.15f + 2f * Math.Clamp(left / lead, 0f, 1f));

        var half = new Vector2(KeySize / 2f);
        var key = new UIBox2(center - half, center + half);

        // backdrop
        handle.DrawRect(new UIBox2(center - new Vector2(150f, 110f), center + new Vector2(150f, 105f)), Panel);

        var title = Loc.GetString("st14-bug-grab-ui-title");
        var shake = new Vector2(MathF.Sin(time * 40f), MathF.Cos(time * 33f)) * 1.5f;
        DrawCentered(handle, _textFont, title, center + new Vector2(0f, -92f) + shake, Danger, 1.4f);

        // the key and its ring
        handle.DrawRect(key, onTime ? Perfect.WithAlpha(0.35f) : Panel);
        handle.DrawRect(key, onTime ? Perfect : Edge, false);
        if (left > -grabbed.PerfectWindow.TotalSeconds)
            handle.DrawCircle(center, ringRadius, onTime ? Perfect : Color.White.WithAlpha(0.85f), false);

        DrawCentered(handle, _keyFont, KeyName(grabbed.Prompt), center, onTime ? Perfect : Color.White);

        // how close the trooper is to breaking free
        var progress = Math.Clamp(grabbed.Struggle / grabbed.StruggleNeeded, 0f, 1f);
        var bar = new UIBox2(center + new Vector2(-120f, 62f), center + new Vector2(120f, 76f));
        handle.DrawRect(bar, Color.Black.WithAlpha(0.7f));
        handle.DrawRect(new UIBox2(bar.TopLeft, new Vector2(bar.Left + bar.Width * progress, bar.Bottom)),
            Color.InterpolateBetween(Danger, Perfect, progress));
        handle.DrawRect(bar, Edge, false);

        DrawCentered(handle, _textFont, Loc.GetString("st14-bug-grab-ui-hint"), center + new Vector2(0f, 92f), Color.White.WithAlpha(0.8f));
    }

    private static void DrawCentered(DrawingHandleScreen handle, Font font, string text, Vector2 center, Color color, float scale = 1f)
    {
        var size = handle.GetDimensions(font, text, scale);
        handle.DrawString(font, center - size / 2f, text, scale, color);
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
