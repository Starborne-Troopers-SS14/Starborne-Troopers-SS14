using System.Numerics;
using Content.Shared._ST14.Terrain;
using Content.Shared.Chasm;
using Content.Shared.Gravity;
using Content.Shared.Projectiles;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._ST14.Terrain;

public sealed partial class ST14PitSystem : EntitySystem
{
    [Dependency] private ChasmSystem _chasm = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14PitComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ST14PitComponent, EndCollideEvent>(OnEndCollide);
    }

    // Collide events come per fixture pair, so count them: the walker leaves only when the last one ends.
    private void OnStartCollide(Entity<ST14PitComponent> ent, ref StartCollideEvent args)
    {
        // like a chasm, only what really walks there: no walls, no bullets, nothing without a hard body
        if (args.OtherBody.BodyType == BodyType.Static || !args.OtherFixture.Hard)
            return;

        ent.Comp.Touching[args.OtherEntity] = ent.Comp.Touching.GetValueOrDefault(args.OtherEntity) + 1;
    }

    private void OnEndCollide(Entity<ST14PitComponent> ent, ref EndCollideEvent args)
    {
        if (!ent.Comp.Touching.TryGetValue(args.OtherEntity, out var count))
            return;

        if (count <= 1)
            ent.Comp.Touching.Remove(args.OtherEntity);
        else
            ent.Comp.Touching[args.OtherEntity] = count - 1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ST14PitComponent, ChasmComponent, FixturesComponent>();
        while (query.MoveNext(out var uid, out var pit, out var chasm, out var fixtures))
        {
            if (pit.Touching.Count == 0)
                continue;

            var inv = _transform.GetInvWorldMatrix(uid);
            List<EntityUid>? done = null;
            foreach (var other in pit.Touching.Keys)
            {
                if (TerminatingOrDeleted(other) || HasComp<ChasmFallingComponent>(other))
                {
                    (done ??= new()).Add(other);
                    continue;
                }

                var local = Vector2.Transform(_transform.GetWorldPosition(other), inv);
                if (!Inside(fixtures, local) || !CanFall((uid, chasm), other))
                    continue;

                if (_chasm.StartFalling((uid, chasm), other) != null)
                    (done ??= new()).Add(other);
            }

            if (done == null)
                continue;

            foreach (var other in done)
            {
                pit.Touching.Remove(other);
            }
        }
    }

    /// <summary>
    /// The checks a chasm makes through its step trigger: nothing flying or thrown over it, nothing weightless,
    /// nothing a grappling hook holds, what its lists allow, and nothing another system keeps from falling
    /// </summary>
    private bool CanFall(Entity<ChasmComponent> pit, EntityUid other)
    {
        if (HasComp<ProjectileComponent>(other) || HasComp<ThrownItemComponent>(other))
            return false;

        if (TryComp<PhysicsComponent>(other, out var physics) && physics.BodyStatus == BodyStatus.InAir)
            return false;

        if (_gravity.IsWeightless(other))
            return false;

        if (!(pit.Comp.Whitelist == null && pit.Comp.Blacklist == null ||
              _whitelist.CheckBoth(other, pit.Comp.Blacklist, pit.Comp.Whitelist)))
        {
            return false;
        }

        var step = new StepTriggerAttemptEvent { Source = pit, Tripper = other };
        RaiseLocalEvent(pit, ref step);
        if (step.Cancelled)
            return false;

        var attempt = new EntityStartFallingAttemptEvent(other);
        RaiseLocalEvent(pit, ref attempt);
        return !attempt.Cancelled;
    }

    /// <summary>
    /// Whether a point in the pit's local space is inside one of its fixtures
    /// </summary>
    private static bool Inside(FixturesComponent fixtures, Vector2 local)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0).Contains(local))
                return true;
        }

        return false;
    }
}
