/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Robust.Shared.Analyzers;
using Content.Client._ST14.ZLevels.Core.Overlays;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared.Camera;
using Content.Shared.StatusEffectNew.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client._ST14.ZLevels.Core;

/// <summary>
/// Only process Eye offset and drawdepth on clientside
/// </summary>
public sealed partial class ST14ClientZLevelsSystem : ST14SharedZLevelsSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IEyeManager _eye = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new ST14ZLevelBlurOverlay());
        _overlay.AddOverlay(new ST14ZLevelFogOverlay());
    }

    [SubscribeLocalEvent]
    private void OnEyeOffset(Entity<ST14ZPhysicsComponent> ent, ref GetEyeOffsetEvent args)
    {
        Angle rotation = _eye.CurrentEye.Rotation * -1;
        var localPosition = GetVisualLocalPosition(ent, ent.Comp, Transform(ent), ZPhysicsQuery);
        var offset = rotation.RotateVec(new Vector2(0, localPosition * ZLevelOffset));
        args.Offset += offset;
    }

    /// <summary>
    /// Entities riding/parented onto another Z-physics body (e.g. a rider buckled to a flying vehicle)
    /// never step their own LocalPosition, so they must visually follow the parent's height instead.
    /// </summary>
    internal static float GetVisualLocalPosition(EntityUid uid, ST14ZPhysicsComponent zPhys, TransformComponent xform, EntityQuery<ST14ZPhysicsComponent> zPhysicsQuery)
    {
        if (xform.ParentUid != xform.MapUid && zPhysicsQuery.TryComp(xform.ParentUid, out var parentZPhys))
            return parentZPhys.LocalPosition;

        return zPhys.LocalPosition;
    }

    [SubscribeLocalEvent]
    private void OnStartup(Entity<ST14ZPhysicsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (sprite.SnapCardinals)
            return;

        ent.Comp.DrawDepthDefault = sprite.DrawDepth;
        ent.Comp.SpriteOffsetDefault = sprite.Offset;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<ST14ZLevelBlurOverlay>();
        _overlay.RemoveOverlay<ST14ZLevelFogOverlay>();
    }

    [Dependency] private SharedMapSystem _mapSys = default!;

    private readonly record struct TileScanKey(EntityUid Grid, Vector2i BottomLeft, Vector2i TopRight, int TileVersion);

    // ST14: last scan result per map. The viewport asks every frame for every level it might draw.
    private readonly Dictionary<EntityUid, (TileScanKey Key, bool Result)> _tileScanCache = new();

    /// <summary>
    /// Whether any tile in the given world rectangle (plus a one-tile margin) of the map is empty or
    /// transparent, i.e. whether the level below can be seen through this one. The result is cached per
    /// map until the rectangle covers other tiles or any tile changes.
    /// </summary>
    public bool AnyEmptyOrTransparentTile(EntityUid mapUid, MapId mapId, Vector2 worldMin, Vector2 worldMax, bool useCache = true)
    {
        if (!_mapSys.TryFindGridAt(mapUid, worldMin, out var gridUid, out var grid))
            return true;

        var bottomLeft = _mapSys.TileIndicesFor(gridUid, grid, new MapCoordinates(worldMin, mapId));
        var topRight = _mapSys.TileIndicesFor(gridUid, grid, new MapCoordinates(worldMax, mapId));
        var key = new TileScanKey(gridUid, bottomLeft, topRight, TileVersion);

        if (useCache && _tileScanCache.TryGetValue(mapUid, out var cached) && cached.Key == key)
            return cached.Result;

        var result = ScanForEmptyOrTransparentTile(gridUid, grid, bottomLeft, topRight);
        if (useCache)
            _tileScanCache[mapUid] = (key, result);

        return result;
    }

    private bool ScanForEmptyOrTransparentTile(EntityUid gridUid, MapGridComponent grid, Vector2i bottomLeft, Vector2i topRight)
    {
        for (var x = bottomLeft.X - 1; x <= topRight.X + 1; x++)
        {
            for (var y = bottomLeft.Y - 1; y <= topRight.Y + 1; y++)
            {
                var tile = _mapSys.GetTileRef(gridUid, grid, new Vector2i(x, y));
                var tileDef = (ContentTileDefinition) TilDefMan[tile.Tile.TypeId];
                if (tileDef.Transparent || tile.Tile.IsEmpty)
                    return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Pre-animation pass for Z-level visuals.
/// Runs its <see cref="FrameUpdate"/> BEFORE <see cref="AnimationPlayerSystem"/> every render frame,
/// resetting <see cref="SpriteComponent.Offset"/> to the entity's clean base (no Z).
/// This prevents Z from accumulating across frames when no animation writes to the offset
/// (e.g. entities that only animate scale, like SlimeIceBig).
/// </summary>
internal sealed partial class ST14ClientZLevelsPreAnimSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<ST14ZPhysicsComponent> _zPhysQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        // Phase 1 (per render frame): strip any Z left from last frame so the animation player
        // always starts from a Z-free base, and Phase 2 can add exactly one Z contribution.
        var query = EntityQueryEnumerator<ST14ZPhysicsComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var zPhys, out var sprite, out var xform))
        {
            var localPosition = ST14ClientZLevelsSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);

            // ST14: SetOffset rebuilds the sprite's local matrix every call. Most sprites already sit at
            // their default offset (nothing on them is airborne or animating it), so skip the no-op.
            if (sprite.Offset != zPhys.SpriteOffsetDefault)
                _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault);

            _sprite.SetDrawDepth((uid, sprite), localPosition > 0 ? (int)Shared.DrawDepth.DrawDepth.OverMobs : zPhys.DrawDepthDefault);
        }

        // Set parent-synced status effect offsets to the parent's current Z value each frame — prevents accumulation.
        var syncQuery = EntityQueryEnumerator<StatusEffectComponent, SpriteComponent, TransformComponent>();
        while (syncQuery.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var parent = xform.ParentUid;
            if (_mapGridQuery.HasComp(parent))
                continue;
            if (!_zPhysQuery.TryComp(parent, out var parentZPhys))
                continue;
            var zOffset = new Vector2(0, parentZPhys.LocalPosition * ST14SharedZLevelsSystem.ZLevelOffset);
            if (sprite.Offset != zOffset) // ST14: skip the matrix rebuild when it already sits there
                _sprite.SetOffset((uid, sprite), zOffset);
        }
    }
}

/// <summary>
/// Post-animation pass for Z-level visuals.
/// Runs its <see cref="FrameUpdate"/> AFTER <see cref="AnimationPlayerSystem"/> so that
/// whatever offset the animation player wrote to <see cref="SpriteComponent.Offset"/> this frame
/// (loop animation, one-shot swing, idle bob, etc.) is preserved, and the Z-height contribution
/// is simply added on top.  No animation code needs to know about Z levels.
/// </summary>
internal sealed partial class ST14ClientZLevelsPostAnimSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private EntityQuery<ST14ZPhysicsComponent> _zPhysQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        // Phase 2: add the Z-height contribution on top of the animation-player's output.
        // At this point sprite.Offset == animationValue (or SpriteOffsetDefault if no anim ran).
        // The offset is counter-rotated by the entity's world angle so it always points world-up,
        // preventing it from orbiting the pivot when the entity has angular velocity (e.g. shurikens).
        var query = EntityQueryEnumerator<ST14ZPhysicsComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var zPhys, out var sprite, out var xform))
        {
            var localPosition = ST14ClientZLevelsSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);

            // ST14: a zero height adds a zero offset; skip the world-rotation walk and the matrix rebuild.
            if (localPosition == 0f)
                continue;

            var rawZ = new Vector2(0, localPosition * ST14SharedZLevelsSystem.ZLevelOffset);
            Vector2 zOffset;
            if (sprite.NoRotation)
                zOffset = rawZ;
            else
                zOffset = new Angle(-_xform.GetWorldRotation(xform)).RotateVec(rawZ);
            _sprite.SetOffset((uid, sprite), sprite.Offset + zOffset);
        }
    }
}
