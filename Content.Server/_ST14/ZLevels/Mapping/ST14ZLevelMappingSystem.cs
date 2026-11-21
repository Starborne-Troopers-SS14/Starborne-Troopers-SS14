/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Core.Components;
using Robust.Shared.Analyzers;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.ZLevels.Mapping;

public sealed partial class ST14ZLevelMappingSystem : EntitySystem
{
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;
    [Dependency] private SharedMapSystem _map = default!;

    [SubscribeLocalEvent]
    private void OnAddedIntoZNetwork(Entity<ST14ZMapComponent> ent, ref ST14MapAddedIntoZNetworkEvent args)
    {
        if (_map.IsInitialized(ent))
            EntityManager.AddComponents(ent, args.Network.Comp.Components);
        else
        {
            var hasInitializedMaps = false;
            foreach (var existingMapUid in args.Network.Comp.ZLevels.Values)
            {
                if (existingMapUid.HasValue && _map.IsInitialized(existingMapUid.Value))
                {
                    hasInitializedMaps = true;
                    break;
                }
            }

            if (hasInitializedMaps)
                _map.InitializeMap(ent.Owner);
        }
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<ST14ZMapComponent> ent, ref MapInitEvent args)
    {
        if (!_zLevels.TryGetMapNetwork(ent, out var network))
            return;

        EntityManager.AddComponents(ent, network.Comp.Components);
    }
}
