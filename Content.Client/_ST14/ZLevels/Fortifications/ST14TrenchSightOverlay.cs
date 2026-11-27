/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Client._ST14.ZLevels.Core;
using Content.Client._ST14.ZLevels.Roof;
using Content.Shared._ST14.ZLevels.Core.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._ST14.ZLevels.Fortifications;

/// <summary>
/// From inside a dug-out, under its roof, nothing of the surface is seen: the ground around is dark, and all on it.
/// The soldier sees out only through the way in, along the trench. Round a corner of a trench the vanilla field of view
/// does the work (the trench sides are occluders).
/// </summary>
public sealed class ST14TrenchSightOverlay : Overlay
{
    /// <summary>
    /// How dark the surface is from inside a dug-out
    /// </summary>
    private const float DugoutShade = 0.97f;

    private readonly IEntityManager _entMan;
    private readonly IPlayerManager _player;
    private readonly SharedTransformSystem _transform;
    private readonly SharedMapSystem _map;
    private readonly ST14ClientZLevelsSystem _zLevels;
    private readonly ST14ClientZLevelsRoofSystem _roofs;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public ST14TrenchSightOverlay(IEntityManager entMan, IPlayerManager player)
    {
        _entMan = entMan;
        _player = player;
        _transform = entMan.System<SharedTransformSystem>();
        _map = entMan.System<SharedMapSystem>();
        _zLevels = entMan.System<ST14ClientZLevelsSystem>();
        _roofs = entMan.System<ST14ClientZLevelsRoofSystem>();
    }

    /// <summary>
    /// The player is in a dug-out: on a level under the ground with a roof over their tile on the surface
    /// </summary>
    private bool TryGetDugout(out EntityUid surface, out Entity<MapGridComponent> ground)
    {
        surface = default;
        ground = default;

        if (_player.LocalEntity is not { } player ||
            !_entMan.TryGetComponent<TransformComponent>(player, out var xform) ||
            xform.MapUid is not { } own ||
            !_entMan.TryGetComponent<ST14ZMapComponent>(own, out var zMap) || zMap.Depth >= 0 ||
            !_zLevels.TryMapUp(own, out var up) || !_zLevels.TryGetLayerGrid(up.Owner, out var grid))
        {
            return false;
        }

        var position = _transform.GetWorldPosition(xform);
        var tile = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
        if (!_roofs.TryGetRoofEntity(up.Owner, tile, out _))
            return false;

        surface = up.Owner;
        ground = grid.Value;
        return true;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // only the surface pass is darkened
        return TryGetDugout(out var surface, out _) && args.MapUid == surface;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!TryGetDugout(out _, out var ground))
            return;

        // every tile of ground on screen goes dark, and all on it; the openings (the trench, the way in) stay as they are
        var color = Color.Black.WithAlpha(DugoutShade);
        var bounds = args.WorldAABB;
        for (var x = (int) MathF.Floor(bounds.Left); x <= (int) MathF.Ceiling(bounds.Right); x++)
        {
            for (var y = (int) MathF.Floor(bounds.Bottom); y <= (int) MathF.Ceiling(bounds.Top); y++)
            {
                var tile = new Vector2i(x, y);
                if (!IsDug(ground, tile))
                    args.WorldHandle.DrawRect(new Box2(tile, tile + Vector2i.One), color);
            }
        }
    }

    /// <summary>
    /// The ground of the surface is dug out there
    /// </summary>
    private bool IsDug(Entity<MapGridComponent> ground, Vector2i worldTile)
    {
        var index = _map.WorldToTile(ground, ground.Comp, worldTile + new Vector2(0.5f));
        return _map.TryGetTileRef(ground, ground.Comp, index, out var tileRef) && tileRef.Tile.IsEmpty;
    }
}
