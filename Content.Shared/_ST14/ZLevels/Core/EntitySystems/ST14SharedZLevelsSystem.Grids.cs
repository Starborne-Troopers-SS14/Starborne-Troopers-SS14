/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using Robust.Shared.Map.Components;

namespace Content.Shared._ST14.ZLevels.Core.EntitySystems;

public abstract partial class ST14SharedZLevelsSystem
{
    /// <summary>
    /// Returns the z-level depth of the map containing the given grid,
    /// or null if the grid's map is not part of a z-level network.
    /// </summary>
    [PublicAPI]
    public int? TryGetGridZDepth(EntityUid gridUid)
    {
        var mapUid = Transform(gridUid).MapUid;
        return mapUid.HasValue && _zMapQuery.TryComp(mapUid.Value, out var zMap) ? zMap.Depth : null;
    }

    /// <summary>
    /// Returns the world tile coordinate for a grid tile (floor of tile-center world position).
    /// Correct for 90°-rotated, tile-snapped grids — tile centers are always at half-integer world coords.
    /// </summary>
    [PublicAPI]
    public Vector2i GridTileToWorldTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var c = _map.GridTileToWorldPos(gridUid, grid, tile);
        return new Vector2i((int)MathF.Floor(c.X), (int)MathF.Floor(c.Y));
    }

    /// <summary>
    /// ST14: the ground grid of a z-level. Layers made by ST14 are planet maps (the map entity is the grid);
    /// a sector's main level is a map with its ground as a separate grid on it. Returns the map itself when
    /// it is a grid, otherwise the biggest grid sitting directly on the map.
    /// </summary>
    [PublicAPI]
    public bool TryGetLayerGrid(EntityUid mapUid, [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        grid = null;
        if (_gridQuery.TryComp(mapUid, out var mapGrid))
        {
            grid = (mapUid, mapGrid);
            return true;
        }

        var best = 0;
        var query = AllEntityQuery<MapGridComponent, TransformComponent>(); // paused maps too
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.ParentUid != mapUid || comp.ChunkCount < best && grid != null)
                continue;

            best = comp.ChunkCount;
            grid = (uid, comp);
        }

        return grid != null;
    }
}

