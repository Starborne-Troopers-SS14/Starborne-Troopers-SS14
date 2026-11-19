using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Client.Clickable;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Client.ComponentTrees;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Client._ST14.ZLevelShooting;

public sealed partial class ST14ZTargetingSystem : EntitySystem
{
    [Dependency] private ClickableSystem _clickable = default!;
    [Dependency] private CEClientZLevelsSystem _zLevels = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SpriteTreeSystem _spriteTree = default!;
    [Dependency] private EntityQuery<CEZLevelViewerComponent> _viewerQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;

    public EntityUid? GetTarget(EntityUid shooter, IEye eye, MapCoordinates mouse)
    {
        if (Transform(shooter).MapUid is not { } map)
            return null;

        var shift = (-eye.Rotation).ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset;
        var above = _viewerQuery.TryComp(shooter, out var viewer) && viewer.LookUp
            ? _zLevels.GetVisibleZLevelsAbove(shooter)
            : 0;

        for (var depth = above; depth > 0; depth--)
        {
            if (TryGetTarget(map, depth, mouse.Position, shift, eye, out var target))
                return target;
        }

        if (TryGetTarget(map, 0, mouse.Position, shift, eye, out _))
            return null;

        for (var depth = -1; depth >= -CESharedZLevelsSystem.MaxZLevelsBelowRendering; depth--)
        {
            if (TryGetTarget(map, depth, mouse.Position, shift, eye, out var target))
                return target;
        }

        return null;
    }

    private bool TryGetTarget(EntityUid map, int depth, Vector2 mouse, Vector2 shift, IEye eye, out EntityUid target)
    {
        target = default;

        var level = map;
        if (depth != 0)
        {
            if (!_zLevels.TryMapOffset(map, depth, out var other))
                return false;

            level = other.Owner;
        }

        if (!_mapQuery.TryComp(level, out var mapComp) || IsCovered(level, depth, mouse, shift))
            return false;

        var position = mouse + shift * depth;
        var found = false;
        var bestAlive = false;
        var bestDepth = int.MinValue;
        var bestOrder = 0u;

        foreach (var entry in _spriteTree.QueryAabb(mapComp.MapId, Box2.CenteredAround(position, Vector2.One)))
        {
            if (!CanHit(entry.Uid) ||
                !_clickable.CheckClick((entry.Uid, null, entry.Component, entry.Transform), position, eye, true, out var drawDepth, out var renderOrder, out _))
                continue;

            var alive = _mobState.IsAlive(entry.Uid);
            if (found && (alive, drawDepth, renderOrder).CompareTo((bestAlive, bestDepth, bestOrder)) <= 0)
                continue;

            found = true;
            target = entry.Uid;
            bestAlive = alive;
            bestDepth = drawDepth;
            bestOrder = renderOrder;
        }

        return found;
    }

    private bool IsCovered(EntityUid level, int depth, Vector2 mouse, Vector2 shift)
    {
        for (var below = depth; below < 0; below++)
        {
            if (_zLevels.HasOpaqueAbove(mouse + shift * (below + 1), level))
                return true;

            if (!_zLevels.TryMapUp(level, out var up))
                return false;

            level = up.Owner;
        }

        return false;
    }

    private bool CanHit(EntityUid uid)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return false;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.BulletImpassable) != 0)
                return true;
        }

        return false;
    }
}
