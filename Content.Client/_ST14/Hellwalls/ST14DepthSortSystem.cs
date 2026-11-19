using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.Hellwalls;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._ST14.Hellwalls;

public sealed partial class ST14DepthSortSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SpriteTreeSystem _spriteTree = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private CEClientZLevelsSystem _zLevels = default!;
    [Dependency] private EntityQuery<ST14DepthSortComponent> _sortQuery = default!;
    [Dependency] private EntityQuery<CEZPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<MapComponent> _mapQuery = default!;

    private const int MobsDepth = (int) Content.Shared.DrawDepth.DrawDepth.Mobs;
    private const int OverMobsDepth = (int) Content.Shared.DrawDepth.DrawDepth.OverMobs;

    private readonly List<Entity<SpriteComponent, TransformComponent>> _sprites = new();
    private readonly List<(float Depth, SpriteComponent Sprite)> _order = new();

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(CEClientZLevelsPreAnimSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        var eye = _eye.CurrentEye;
        var mapUid = _map.GetMapOrInvalid(eye.Position.MapId);
        if (!mapUid.IsValid())
            return;

        var down = (-eye.Rotation).RotateVec(-Vector2.UnitY);
        var bounds = _eye.GetWorldViewbounds().CalcBoundingBox();
        var shift = (-eye.Rotation).ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset;

        Sort(eye.Position.MapId, bounds, down);

        for (var depth = -1; _zLevels.TryMapOffset(mapUid, depth, out var level); depth--)
        {
            SortLevel(level, bounds.Translated(shift * depth), down);
        }

        for (var depth = 1; _zLevels.TryMapOffset(mapUid, depth, out var level); depth++)
        {
            SortLevel(level, bounds.Translated(shift * depth), down);
        }
    }

    private void SortLevel(EntityUid level, Box2 bounds, Vector2 down)
    {
        if (_mapQuery.TryComp(level, out var map))
            Sort(map.MapId, bounds, down);
    }

    private void Sort(MapId mapId, Box2 bounds, Vector2 down)
    {
        _sprites.Clear();
        _order.Clear();
        _spriteTree.QueryAabb(_sprites, mapId, bounds);

        foreach (var (uid, sprite, xform) in _sprites)
        {
            if (_sortQuery.TryComp(uid, out var sort))
                _order.Add((GetDepth(xform, sort, down), sprite));
            else if (IsMob(uid, sprite))
                _order.Add((Vector2.Dot(_transform.GetWorldPosition(xform), down), sprite));
        }

        _order.Sort(static (a, b) => a.Depth.CompareTo(b.Depth));

        uint rank = 0;
        for (var i = 0; i < _order.Count; i++)
        {
            if (i > 0 && _order[i].Depth > _order[i - 1].Depth)
                rank++;

            _order[i].Sprite.RenderOrder = rank;
        }
    }

    private bool IsMob(EntityUid uid, SpriteComponent sprite)
    {
        if (sprite.DrawDepth == MobsDepth)
            return true;

        if (sprite.DrawDepth != OverMobsDepth ||
            !_zPhysQuery.TryComp(uid, out var zPhys) ||
            zPhys.DrawDepthDefault != MobsDepth)
            return false;

        _sprite.SetDrawDepth((uid, sprite), MobsDepth);
        return true;
    }

    private float GetDepth(TransformComponent xform, ST14DepthSortComponent sort, Vector2 down)
    {
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);
        var depth = Vector2.Dot(position, down);

        foreach (var extent in sort.Extents)
        {
            depth = MathF.Min(depth, Vector2.Dot(position + rotation.RotateVec(extent), down));
        }

        return depth + sort.Offset;
    }
}
