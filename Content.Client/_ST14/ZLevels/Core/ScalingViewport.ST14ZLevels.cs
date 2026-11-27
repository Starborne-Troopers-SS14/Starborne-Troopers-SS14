/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;

    private ST14ClientZLevelsSystem? _zLevels;
    private SharedMapSystem? _mapSystem;

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;

    // ST14: per-depth reusable eyes for RenderZLevels
    private readonly Dictionary<int, ZEye> _zEyes = new();
    private EntityQuery<ST14ZLevelViewerComponent>? _viewerQuery;

    /// <summary>
    /// We are looking for at least one empty tile on the screen.
    /// This is used to ensure that it makes sense to draw the z-planes and that they are visible.
    /// </summary>
    public bool TryFindEmptyTiles(EntityUid mapUid)
    {
        if (_xformQuery is null || !_xformQuery.Value.TryComp(mapUid, out var xform))
            return true;

        var mapId = xform.MapID;
        var (min, max) = ViewBounds();

        // ST14: the tile scan moved to the z-level system, which caches it until the view crosses a tile
        // border or a tile changes. Standing on solid floor it used to read every tile on screen every frame.
        if (_zLevels is null)
            return true;

        return _zLevels.AnyEmptyOrTransparentTile(mapUid, mapId, min, max);
    }

    /// <summary>
    /// The world rectangle on screen
    /// </summary>
    private (Vector2 Min, Vector2 Max) ViewBounds()
    {
        var drawBox = GetDrawBox();

        // ST14: four locals instead of a heap array, this runs for every rendered level every frame.
        // No stackalloc: the client sandbox cannot verify it.
        var bl = _eyeManager.ScreenToMap(drawBox.BottomLeft).Position;
        var br = _eyeManager.ScreenToMap(drawBox.BottomRight).Position;
        var tl = _eyeManager.ScreenToMap(drawBox.TopLeft).Position;
        var tr = _eyeManager.ScreenToMap(drawBox.TopRight).Position;

        var minX = MathF.Min(MathF.Min(bl.X, br.X), MathF.Min(tl.X, tr.X));
        var minY = MathF.Min(MathF.Min(bl.Y, br.Y), MathF.Min(tl.Y, tr.Y));
        var maxX = MathF.Max(MathF.Max(bl.X, br.X), MathF.Max(tl.X, tr.X));
        var maxY = MathF.Max(MathF.Max(bl.Y, br.Y), MathF.Max(tl.Y, tr.Y));

        return (new Vector2(minX, minY), new Vector2(maxX, maxY));
    }

    private void RenderZLevels(IClydeViewport viewport)
    {
        if (_eye is null)
            return;

        _fallbackEye = _eye;

        // Cache frequently accessed components/systems
        _xformQuery ??= _entityManager.GetEntityQuery<TransformComponent>();
        _mapQuery ??= _entityManager.GetEntityQuery<MapComponent>();

        // Cache systems and components
        _zLevels ??= _entityManager.System<ST14ClientZLevelsSystem>();
        _mapSystem ??= _entityManager.System<SharedMapSystem>();

        if (_player.LocalEntity is null)
            return;

        _viewerQuery ??= _entityManager.GetEntityQuery<ST14ZLevelViewerComponent>(); // ST14: cached query

        if (!_viewerQuery.Value.TryComp(_player.LocalEntity.Value, out var zLevelViewer))
            return;

        if (!_xformQuery.Value.TryComp(_player.LocalEntity, out var playerXform))
            return;

        if (playerXform.MapUid is null)
            return;

        var lookUp = 0;
        if (zLevelViewer.LookUp)
        {
            lookUp = _zLevels.GetVisibleZLevelsAbove(_player.LocalEntity.Value, playerXform.MapUid);
        }
        else
        {
            // ST14: like in a trench line, the level above is drawn over the player's own: from a trench the surface
            // around it, from the surface the bunker roofs on the level above. Bunker roofs over the player are
            // hidden by ST14BunkerRoofVisualsSystem.
            var (min, max) = ViewBounds();
            var playerPos = _xformQuery.Value.GetComponent(_player.LocalEntity.Value).WorldPosition;
            if (_zLevels.ShouldDrawLevelAbove(playerXform.MapUid.Value, playerPos, min, max))
                lookUp = 1;
        }

        var lowestDepth = 0;
        for (var i = 0; i >= -ST14SharedZLevelsSystem.MaxZLevelsBelowRendering; i--)
        {
            var checkingMap = playerXform.MapUid.Value;

            if (i != 0)
            {
                if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, i, out var mapUidBelow))
                    continue;

                checkingMap = mapUidBelow;
            }

            lowestDepth = i;

            if (!TryFindEmptyTiles(checkingMap))
                break;
        }

        // ST14: overlays drawn once over everything (the weather) draw only on the last level rendered
        ST14TopDepth = lookUp;

        //From the lowest depth to the highest, render each level
        for (var depth = lowestDepth; depth <= lookUp; depth++)
        {
            if (depth == 0)
                viewport.Eye = _fallbackEye;
            else
            {
                if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, depth, out var mapUidBelow))
                    continue;

                if (!_mapQuery.Value.TryComp(mapUidBelow, out var mapComp))
                    continue;

                Angle rotation = _fallbackEye.Rotation * -1;
                var offset = rotation.ToWorldVec() * ST14ClientZLevelsSystem.ZLevelOffset *
                             DrawnHeight(playerXform.MapUid.Value, mapUidBelow, depth);

                // ST14: reuse one ZEye per depth instead of allocating a new one per level per frame.
                // Every property the old object initializer set is overwritten here, Eye has no other state.
                if (!_zEyes.TryGetValue(depth, out var zEye))
                {
                    zEye = new ZEye(lowestDepth, depth, lookUp);
                    _zEyes[depth] = zEye;
                }

                zEye.LowestDepth = lowestDepth;
                zEye.Depth = depth;
                zEye.HighestDepth = lookUp;
                zEye.Position = new MapCoordinates(_fallbackEye.Position.Position, mapComp.MapId);
                zEye.DrawFov = _fallbackEye.DrawFov && depth >= 0;
                zEye.DrawLight = _fallbackEye.DrawLight;
                zEye.Offset = _fallbackEye.Offset + offset;
                zEye.Rotation = _fallbackEye.Rotation;
                zEye.Scale = _fallbackEye.Scale;
                viewport.Eye = zEye;
            }

            viewport.ClearColor = depth == lowestDepth ? Color.Black : null;
            viewport.Render();
        }

        // Restore the Eye
        Eye = _fallbackEye;
        viewport.Eye = Eye;
    }

    /// <summary>
    /// ST14: how many levels up <paramref name="level"/> is drawn from the player's own. Levels below the ground
    /// (depth under 0: trenches, tunnels) are drawn flat under the ground, with no shift: a pit is looked straight into
    /// and a soldier in a trench one tile wide is seen whole, not half under its edge. Only levels above the ground
    /// (roofs, sky) stand out by <see cref="ST14ClientZLevelsSystem.ZLevelOffset"/> each.
    /// </summary>
    private int DrawnHeight(EntityUid playerMap, EntityUid level, int relativeDepth)
    {
        if (!_entityManager.TryGetComponent<ST14ZMapComponent>(playerMap, out var own) ||
            !_entityManager.TryGetComponent<ST14ZMapComponent>(level, out var other))
        {
            return relativeDepth;
        }

        return Math.Max(other.Depth, 0) - Math.Max(own.Depth, 0);
    }

    /// <summary>
    /// ST14: the depth of the last level rendered this frame, relative to the player (0: their own). An overlay drawn
    /// once over everything draws only on it, see <see cref="IsTopLevel"/>.
    /// </summary>
    public static int ST14TopDepth;

    /// <summary>
    /// ST14: whether a render with <paramref name="eye"/> is the last level of the frame, the one on top
    /// </summary>
    public static bool IsTopLevel(IEye? eye)
    {
        var depth = eye is ZEye z ? z.Depth : 0;
        return depth == ST14TopDepth;
    }

    public sealed partial class ZEye(int lowest, int depth, int high) : Robust.Shared.Graphics.Eye
    {
        public int LowestDepth = lowest;
        public int Depth = depth;
        public int HighestDepth = high;
    }
}
