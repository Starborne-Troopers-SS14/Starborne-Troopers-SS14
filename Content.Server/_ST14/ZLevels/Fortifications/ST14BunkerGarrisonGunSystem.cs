/*
 * Starborne Troopers bunker garrisons on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Timing;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// The guns of the bunker garrisons. Each one keeps to the nearest bug it can see within its range and its arc (the
/// rifles every way, the machine gun and the heavy gun within the arc they were built facing), turns to it and fires
/// as fast as the gun goes. Bugs are whoever its faction is hostile to; the heavy gun only takes the big ones.
/// </summary>
public sealed partial class ST14BunkerGarrisonGunSystem : EntitySystem
{
    /// <summary>
    /// How often a gun looks round for a new target
    /// </summary>
    private static readonly TimeSpan LookInterval = TimeSpan.FromSeconds(0.4);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var guns = EntityQueryEnumerator<ST14BunkerGarrisonGunComponent, GunComponent, TransformComponent>();
        while (guns.MoveNext(out var uid, out var garrison, out var gun, out var xform))
        {
            if (garrison.Target is { } kept && !CanTake((uid, garrison), xform, kept))
                garrison.Target = null;

            if (garrison.Target == null && now >= garrison.NextLook)
            {
                garrison.NextLook = now + LookInterval;
                garrison.Target = Look((uid, garrison), xform);
            }

            if (garrison.Target is not { } target)
                continue;

            var to = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(xform);
            _transform.SetWorldRotation(uid, to.ToWorldAngle());
            _guns.AttemptShoot(uid, (uid, gun), Transform(target).Coordinates, target);
        }
    }

    /// <summary>
    /// The nearest bug the gun can take, if any
    /// </summary>
    private EntityUid? Look(Entity<ST14BunkerGarrisonGunComponent> gun, TransformComponent xform)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var here = _transform.GetWorldPosition(xform);
        foreach (var hostile in _factions.GetNearbyHostiles(gun.Owner, gun.Comp.Range))
        {
            if (!CanTake(gun, xform, hostile))
                continue;

            var distance = (_transform.GetWorldPosition(hostile) - here).LengthSquared();
            if (distance < bestDistance)
            {
                best = hostile;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// <paramref name="target"/> is alive, one the gun fires at, on its level, within its range and arc, and in sight
    /// </summary>
    public bool CanTake(Entity<ST14BunkerGarrisonGunComponent> gun, TransformComponent xform, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !_mobState.IsAlive(target))
            return false;

        var targetXform = Transform(target);
        if (targetXform.MapUid != xform.MapUid)
            return false;

        if (gun.Comp.Targets != null && !_whitelist.IsValid(gun.Comp.Targets, target))
            return false;

        var to = _transform.GetWorldPosition(targetXform) - _transform.GetWorldPosition(xform);
        var distance = to.Length();
        if (distance > gun.Comp.Range)
            return false;

        if (gun.Comp.Arc < 360f && distance > 0.01f)
        {
            var facing = gun.Comp.Facing.GetCardinalDir().ToVec();
            var cos = Vector2.Dot(facing, to / distance);
            var halfArc = MathF.PI * gun.Comp.Arc / 360f;
            if (cos < MathF.Cos(halfArc))
                return false;
        }

        return _examine.InRangeUnOccluded(gun.Owner, target, gun.Comp.Range);
    }
}
