using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.ZLevelShooting;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._ST14.ZLevelShooting;

public sealed partial class ST14ZProjectileSystem : EntitySystem
{
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityQuery<CEZPhysicsComponent> _zPhysQuery = default!;

    private const float SameHeight = 0.05f;
    private const float AimTolerance = 1.5f;
    private const float Overshoot = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TargetedProjectileComponent, ProjectileShotEvent>(OnShot);
    }

    private void OnShot(Entity<TargetedProjectileComponent> ent, ref ProjectileShotEvent args)
    {
        var target = ent.Comp.Target;
        if (TerminatingOrDeleted(target) ||
            !TryComp<ProjectileComponent>(ent, out var projectile) ||
            projectile.Shooter is not { } shooter ||
            !TryComp<PhysicsComponent>(ent, out var physics))
            return;

        var xform = Transform(ent);
        var targetXform = Transform(target);
        if (xform.MapUid is not { } map ||
            targetXform.MapUid is not { } targetMap ||
            !_zLevels.TryGetZLevelOffset(map, targetMap, out var offset))
            return;

        var from = GetHeight(shooter) - offset;
        var to = GetHeight(target);
        if (offset == 0 && MathF.Abs(from - to) < SameHeight)
            return;

        var start = _transform.GetWorldPosition(xform);
        var end = _transform.GetWorldPosition(targetXform);
        if (!IsAimedAt(projectile.Weapon, end, MathF.Abs(from - to)))
            return;

        var velocity = physics.LinearVelocity;
        if (offset != 0 && !_zLevels.TryMove(ent, offset))
            return;

        var distance = Vector2.Distance(start, end);
        var direction = distance > 0.01f ? (end - start) / distance : velocity.Normalized();

        _physics.SetLinearVelocity(ent, direction * velocity.Length(), body: physics);
        _transform.SetWorldRotation(ent, direction.ToWorldAngle() + projectile.Angle);

        var flight = EnsureComp<ST14ZProjectileComponent>(ent);
        flight.Target = target;
        flight.Origin = start;
        flight.Distance = distance;
        flight.From = from;
        flight.To = to;
        Dirty(ent, flight);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ST14ZProjectileComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flight, out var xform))
        {
            if (flight.Target is not { } target)
                continue;

            if (!TerminatingOrDeleted(target) &&
                Vector2.Distance(_transform.GetWorldPosition(xform), flight.Origin) <= flight.Distance + Overshoot)
                continue;

            flight.Target = null;
            Dirty(uid, flight);
        }
    }

    private bool IsAimedAt(EntityUid? weapon, Vector2 target, float height)
    {
        if (!TryComp<GunComponent>(weapon, out var gun) || gun.ShootCoordinates is not { } coordinates)
            return false;

        var aim = _transform.ToMapCoordinates(coordinates).Position;
        return Vector2.Distance(aim, target) <= height * CESharedZLevelsSystem.ZLevelOffset + AimTolerance;
    }

    private float GetHeight(EntityUid uid)
    {
        return _zPhysQuery.TryComp(uid, out var zPhys) ? zPhys.LocalPosition : 0f;
    }
}
