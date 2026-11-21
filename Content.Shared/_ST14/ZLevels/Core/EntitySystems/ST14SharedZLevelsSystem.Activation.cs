/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._ST14.ZLevels.Core.Components;
using JetBrains.Annotations;
using Robust.Shared.Analyzers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._ST14.ZLevels.Core.EntitySystems;

public abstract partial class ST14SharedZLevelsSystem
{
    private readonly List<EntityUid> _activeBodies = new();

    // ST14: the set is the truth ("is this body awake"), O(1) for wake and sleep. The list is only the
    // iteration order; bodies put to sleep are dropped from it lazily, in one O(n) compaction per step,
    // instead of an O(n) List.Remove for every body that falls asleep.
    private readonly HashSet<EntityUid> _activeBodiesSet = new();

    public IReadOnlyCollection<EntityUid> ActiveBodies => _activeBodiesSet;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<ST14ZPhysicsComponent> entity, ref MapInitEvent args)
    {
        RefreshBody(entity);

        var mapUid = Transform(entity).MapUid;

        if (!_zMapQuery.TryComp(mapUid, out var zLevel))
            return;

        if (entity.Comp.CurrentZLevel == zLevel.Depth)
            return;

        entity.Comp.CurrentZLevel = zLevel.Depth;
        DirtyField(entity, entity.Comp, nameof(ST14ZPhysicsComponent.CurrentZLevel));
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<ST14ZPhysicsComponent> entity, ref ComponentShutdown args)
    {
        SleepBody((entity, entity));
    }

    [SubscribeLocalEvent]
    private void OnAnchorStateChanged(Entity<ST14ZPhysicsComponent> entity, ref AnchorStateChangedEvent args)
    {
        RefreshBody(entity);
    }

    [SubscribeLocalEvent]
    private void OnPhysicsBodyTypeChanged(Entity<ST14ZPhysicsComponent> entity, ref PhysicsBodyTypeChangedEvent args)
    {
        RefreshBody(entity);
    }

    [SubscribeLocalEvent]
    private void OnParentChanged(Entity<ST14ZPhysicsComponent> entity, ref EntParentChangedMessage args)
    {
        RefreshBody(entity);

        if (ZPhysicsQuery.TryComp(args.OldParent, out var oldParentPhysics))
        {
            SetZPosition((entity, entity), oldParentPhysics.LocalPosition);
            return;
        }

        if (ZPhysicsQuery.HasComp(Transform(entity).ParentUid))
        {
            SetZPosition((entity, entity), 0);
        }
    }

    [PublicAPI]
    public void WakeBody(Entity<ST14ZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        if (!_activeBodiesSet.Add(entity)) // ST14: O(1)
            return;

        entity.Comp.Sleeping = false;
        entity.Comp.SleepTimer = 0f;

        // ST14: a body that fell asleep and woke up again before the next compaction is still listed
        if (entity.Comp.InActiveList)
            return;

        entity.Comp.InActiveList = true;
        _activeBodies.Add(entity);
    }

    [PublicAPI]
    public void SleepBody(Entity<ST14ZPhysicsComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        entity.Comp.Sleeping = true;
        entity.Comp.SleepTimer = 0f;

        // ST14: O(1); the list entry is dropped by CompactActiveBodies on the next step
        _activeBodiesSet.Remove(entity);
    }

    [PublicAPI]
    public void RefreshBody(Entity<ST14ZPhysicsComponent> entity)
    {
        if (TerminatingOrDeleted(entity))
        {
            SleepBody((entity, entity));
            return;
        }

        var transform = Transform(entity);
        var parent = transform.ParentUid;

        var onMap = parent == transform.GridUid || parent == transform.MapUid;

        if (!onMap
            || transform.Anchored
            || _physicsQuery.TryComp(entity, out var physics)
            && physics.BodyType == BodyType.Static)
        {
            SleepBody((entity, entity));
            return;
        }

        WakeBody((entity, entity));
    }
}
