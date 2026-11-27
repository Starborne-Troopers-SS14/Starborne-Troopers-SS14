/*
 * Starborne Troopers bunker roofs on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Light.Components;

namespace Content.Server._ST14.ZLevels.Roof;

public sealed partial class ST14ZLevelsRoofSystem
{
    protected override void OnRoofAdded(EntityUid map, Vector2i worldTile)
    {
        SetCoveredBelow(map, worldTile, true);
    }

    protected override void OnRoofRemoved(EntityUid map, Vector2i worldTile)
    {
        if (TerminatingOrDeleted(map))
            return;

        // still covered when the tile it stood on is opaque or something above covers it
        var covered = false;
        if (ZLevel.TryGetLayerGrid(map, out var grid))
        {
            var index = Map.WorldToTile(grid.Value, grid.Value.Comp, worldTile + new Vector2(0.5f));
            covered = Map.TryGetTile(grid.Value.Comp, index, out var tile) && IsOpaque(tile) ||
                      RoofQuery.TryComp(grid.Value, out var roof) && Roof.IsRooved((grid.Value, grid.Value.Comp, roof), index);
        }

        SetCoveredBelow(map, worldTile, covered);
    }

    /// <summary>
    /// Roofs or unroofs a world tile on every level below <paramref name="map"/>, down to the first opaque tile
    /// </summary>
    private void SetCoveredBelow(EntityUid map, Vector2i worldTile, bool covered)
    {
        if (!ZMapQuery.TryComp(map, out var zMap))
            return;

        foreach (var mapBelow in ZLevel.GetAllMapsBelow((map, zMap)))
        {
            if (!ZLevel.TryGetLayerGrid(mapBelow, out var below))
                continue;

            var roofBelow = EnsureComp<RoofComponent>(below.Value);
            var index = Map.WorldToTile(below.Value, below.Value.Comp, worldTile + new Vector2(0.5f));
            Roof.SetRoof((below.Value, below.Value.Comp, roofBelow), index, covered);

            // an opaque tile here roofs everything further down whatever happens above it
            if (Map.TryGetTile(below.Value.Comp, index, out var tile) && IsOpaque(tile))
                covered = true;
        }
    }
}
