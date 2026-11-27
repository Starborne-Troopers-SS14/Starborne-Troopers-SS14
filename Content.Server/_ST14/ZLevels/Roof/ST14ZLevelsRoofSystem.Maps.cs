/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using System.Numerics;
using Content.Server._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Light.Components;

namespace Content.Server._ST14.ZLevels.Roof;

public sealed partial class ST14ZLevelsRoofSystem
{
    [SubscribeLocalEvent]
    private void OnMapNetworkUpdated(ST14ZLevelMapNetworkUpdatedEvent args)
    {
        if (!TryComp<ST14ZMapNetworkComponent>(args.Network, out var network))
            return;

        RecalculateMapRoofs((args.Network, network));
    }

    /// <summary>
    /// Rebuilds the roofs of every level from the top down (ST14: matched by world tile, see the shared system)
    /// </summary>
    public void RecalculateMapRoofs(Entity<ST14ZMapNetworkComponent> network)
    {
        _roofMap.Clear();

        foreach (var map in network.Comp.ZLevels
                     .OrderByDescending(kv => kv.Key)
                     .Select(kv => kv.Value)
                     .Where(uid => uid.HasValue)
                     .Select(uid => uid!.Value))
        {
            if (!ZLevel.TryGetLayerGrid(map, out var grid))
                continue;

            var gridUid = grid.Value.Owner;
            var gridComp = grid.Value.Comp;
            var roofComp = EnsureComp<RoofComponent>(gridUid);
            var opaque = new List<Vector2i>();
            var enumerator = Map.GetAllTilesEnumerator(gridUid, gridComp);
            while (enumerator.MoveNext(out var tileRef))
            {
                var worldTile = ZLevel.GridTileToWorldTile(gridUid, gridComp, tileRef.Value.GridIndices);
                Roof.SetRoof((gridUid, gridComp, roofComp), tileRef.Value.GridIndices, _roofMap.Contains(worldTile));

                if (IsOpaque(tileRef.Value.Tile))
                    opaque.Add(worldTile);
            }

            // ST14: bunker roofs stand on open tiles of this level and cover what is below them
            if (RoofEntitiesOn(map) is { } roofs)
                opaque.AddRange(roofs.Keys);

            _roofMap.UnionWith(opaque);
        }

        _roofMap.Clear();
    }
}
