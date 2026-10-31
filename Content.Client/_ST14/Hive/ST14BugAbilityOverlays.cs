using System.Numerics;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Systems;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
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
