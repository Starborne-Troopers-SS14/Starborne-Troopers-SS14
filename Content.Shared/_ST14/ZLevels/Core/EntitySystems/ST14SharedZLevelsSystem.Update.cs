/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;

namespace Content.Shared._ST14.ZLevels.Core.EntitySystems;

public abstract partial class ST14SharedZLevelsSystem
{
    public int UpdateCalls { get; private set; }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateCalls = 0;

        if (_net.IsClient && !_clientSimulation)
            return;

        _accumulatedTime += TimeSpan.FromSeconds(frameTime);

        var steps = 0;
        while (_accumulatedTime >= _fixedTimestep && steps < MaxStepsPerFrame)
        {
            UpdateZPhysics((float) _fixedTimestep.TotalSeconds);
            _accumulatedTime -= _fixedTimestep;

            steps++;
        }

        FlushPendingDirty();
    }

    // ST14: bodies whose networked Velocity/LocalPosition changed during this Update's substeps.
    private readonly List<Entity<ST14ZPhysicsComponent>> _pendingDirty = new();

    /// <summary>
    /// ST14: the physics runs several substeps per game tick; each one used to call DirtyField twice
    /// (a string-keyed lookup plus the dirty bookkeeping). All substeps of an Update share the same tick,
    /// so dirtying once at the end produces exactly the same network state.
    /// </summary>
    private void FlushPendingDirty()
    {
        foreach (var ent in _pendingDirty)
        {
            var comp = ent.Comp;
            if (comp.Deleted)
                continue;

            if (comp.PendingDirtyVelocity)
                DirtyField(ent, comp, nameof(ST14ZPhysicsComponent.Velocity));

            if (comp.PendingDirtyPosition)
                DirtyField(ent, comp, nameof(ST14ZPhysicsComponent.LocalPosition));

            comp.PendingDirtyVelocity = false;
            comp.PendingDirtyPosition = false;
        }

        _pendingDirty.Clear();
    }

    private void MarkPendingDirty(Entity<ST14ZPhysicsComponent> ent, bool velocity, bool position)
    {
        var comp = ent.Comp;
        if (!comp.PendingDirtyVelocity && !comp.PendingDirtyPosition)
            _pendingDirty.Add(ent);

        comp.PendingDirtyVelocity |= velocity;
        comp.PendingDirtyPosition |= position;
    }

    /// <summary>
    /// ST14: drops bodies that were put to sleep since the last step, one O(n) pass keeping the order.
    /// </summary>
    private void CompactActiveBodies()
    {
        var write = 0;
        for (var read = 0; read < _activeBodies.Count; read++)
        {
            var uid = _activeBodies[read];
            if (_activeBodiesSet.Contains(uid))
            {
                _activeBodies[write++] = uid;
                continue;
            }

            if (ZPhysicsQuery.TryComp(uid, out var comp))
                comp.InActiveList = false;
        }

        _activeBodies.RemoveRange(write, _activeBodies.Count - write);
    }

    private void UpdateZPhysics(float frameTime)
    {
        UpdateDirtyMovement();
        CompactActiveBodies(); // ST14

        for (var i = _activeBodies.Count - 1; i >= 0; i--)
        {
            // ST14: bodies that the processing of another body put to sleep this step are skipped,
            // as they were when they used to be removed from the list right away.
            if (i >= _activeBodies.Count)
                continue;

            var uid = _activeBodies[i];
            if (!_activeBodiesSet.Contains(uid))
                continue;

            if (!ZPhysicsQuery.TryComp(uid, out var zPhysicsComponent) ||
                !_transformQuery.TryComp(uid, out var xform) ||
                !_physicsQuery.TryComp(uid, out var physics))
            {
                _activeBodiesSet.Remove(uid); // ST14: list entry goes on the next compaction
                continue;
            }

            if (!_zMapQuery.HasComp(xform.MapUid))
            {
                _activeBodiesSet.Remove(uid);
                continue;
            }

            ProcessZPhysics((uid, zPhysicsComponent, physics), frameTime);
        }
    }

    private void ProcessZPhysics(Entity<ST14ZPhysicsComponent, PhysicsComponent> entity, float frameTime)
    {
        var zPhysicsComponent = entity.Comp1;

        if (zPhysicsComponent.Disabled)
            return;

        UpdateCalls++;

        var oldVelocity = zPhysicsComponent.Velocity;
        var oldHeight = zPhysicsComponent.LocalPosition;

        if (zPhysicsComponent.VelocityGravity)
            zPhysicsComponent.Velocity -= ZGravityForce * zPhysicsComponent.GravityMultiplier * frameTime;

        if (zPhysicsComponent.VelocityRaiseEvent)
        {
            var velocityEvent = new ST14GetZVelocityEvent((entity, zPhysicsComponent));
            RaiseLocalEvent(entity, ref velocityEvent);

            zPhysicsComponent.Velocity += velocityEvent.VelocityDelta * frameTime;
        }

        zPhysicsComponent.LocalPosition += zPhysicsComponent.Velocity * frameTime;
        var distanceToGround = zPhysicsComponent.LocalPosition - zPhysicsComponent.CachedGroundHeight;

        if (zPhysicsComponent.AutoStep && distanceToGround < 0)
            zPhysicsComponent.LocalPosition -= distanceToGround;

        if (zPhysicsComponent.CachedStickyGround)
            zPhysicsComponent.LocalPosition -= distanceToGround;

        if (zPhysicsComponent is { Velocity: < 0, Fallable: true })
        {
            if (distanceToGround <= 0.05f)
            {
                if (float.Abs(zPhysicsComponent.Velocity) >= ImpactVelocityLimit)
                {
                    var hitEv = new ST14ZLevelHitEvent(-zPhysicsComponent.Velocity);
                    RaiseLocalEvent(entity, ref hitEv);

                    var land = new LandEvent(null, true);
                    RaiseLocalEvent(entity, ref land);
                }

                if (float.Abs(zPhysicsComponent.Velocity) < zPhysicsComponent.SleepThreshold)
                {
                    zPhysicsComponent.Velocity = 0;
                    zPhysicsComponent.LocalPosition = zPhysicsComponent.CachedGroundHeight;
                }
                else
                {
                    zPhysicsComponent.Velocity = -zPhysicsComponent.Velocity * zPhysicsComponent.Bounciness;
                }
            }
        }

        if (zPhysicsComponent.LocalPosition < 0)
        {
            if (TryMoveDownOrChasm(entity))
            {
                zPhysicsComponent.LocalPosition += 1;
                if (zPhysicsComponent is { CachedStickyGround: false, Fallable: true })
                {
                    var fallEv = new ST14ZLevelFallMapEvent();
                    RaiseLocalEvent(entity, ref fallEv);
                }
            }
        }

        if (zPhysicsComponent.LocalPosition >= 1)
        {
            if (HasTileAbove(entity))
            {
                if (float.Abs(zPhysicsComponent.Velocity) >= ImpactVelocityLimit)
                {
                    var hitEv = new ST14ZLevelHitEvent(zPhysicsComponent.Velocity);
                    RaiseLocalEvent(entity, ref hitEv);

                    var land = new LandEvent(null, true);
                    RaiseLocalEvent(entity, ref land);
                }
                zPhysicsComponent.LocalPosition = 1;
                zPhysicsComponent.Velocity = -zPhysicsComponent.Velocity * zPhysicsComponent.Bounciness;
            }
            else
            {
                if (TryMoveUp(entity))
                    zPhysicsComponent.LocalPosition -= 1;
            }
        }

        if (float.Abs(zPhysicsComponent.Velocity) > ZVelocityLimit)
            zPhysicsComponent.Velocity = float.Sign(zPhysicsComponent.Velocity) * ZVelocityLimit;

        // ST14: dirtied once per Update in FlushPendingDirty instead of once per substep
        var velocityChanged = float.Abs(oldVelocity - zPhysicsComponent.Velocity) > 0.001f;
        var positionChanged = float.Abs(oldHeight - zPhysicsComponent.LocalPosition) > 0.001f;
        if (velocityChanged || positionChanged)
            MarkPendingDirty((entity, zPhysicsComponent), velocityChanged, positionChanged);

        if (zPhysicsComponent.VelocityGravity)
        {
            var targetStatus = distanceToGround > AirborneHeightThreshold ? BodyStatus.InAir : BodyStatus.OnGround;
            if (entity.Comp2.BodyStatus != targetStatus)
            {
                _physicsSystem.SetBodyStatus(entity, entity.Comp2, targetStatus);
                var statusEv = new ST14ZBodyStatusChangedEvent(targetStatus);
                RaiseLocalEvent(entity, ref statusEv);
            }
        }

        SleepUpdate((entity, entity.Comp1), frameTime);
    }

    private void SleepUpdate(Entity<ST14ZPhysicsComponent> entity, float frameTime)
    {
        var distanceToGround = entity.Comp.LocalPosition - entity.Comp.CachedGroundHeight;
        var almostStopped = float.Abs(entity.Comp.Velocity) < entity.Comp.SleepThreshold && float.Abs(distanceToGround) <= 0.01f;

        if (!almostStopped)
        {
            entity.Comp.SleepTimer = 0f;
            return;
        }

        entity.Comp.SleepTimer += frameTime;
        if (entity.Comp.SleepTimer < entity.Comp.TimeToSleep)
            return;

        SleepBody((entity, entity));
    }
}

/// <summary>
/// Raised directed on an entity when its BodyStatus changes due to Z-physics height sync.
/// </summary>
[ByRefEvent]
public readonly record struct ST14ZBodyStatusChangedEvent(BodyStatus NewStatus);
