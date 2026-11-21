/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.Station.Systems;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Analyzers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.ZLevels.Core;

public sealed partial class ST14ZLevelsSystem : ST14SharedZLevelsSystem
{
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private SharedPvsOverrideSystem _pvs = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitView();
    }

    [SubscribeLocalEvent]
    private void OnZMapTerminating(Entity<ST14ZMapComponent> ent, ref EntityTerminatingEvent args)
    {
        var networkUid = ent.Comp.NetworkUid;

        // Round end and DeleteMapNetwork tear the whole network down at once, so there is nothing
        // left to keep consistent. Only a map dying on its own has to be detached.
        if (TerminatingOrDeleted(networkUid) || !TryComp<ST14ZMapNetworkComponent>(networkUid, out var network))
            return;

        TryRemoveMapsFromNetwork((networkUid, network), new[] { ent.Owner });
    }

    /// <summary>
    /// Initializes all uninitialized maps in the z-network.
    /// </summary>
    [PublicAPI]
    public void InitializeZNetwork(Entity<ST14ZMapNetworkComponent> network)
    {
        foreach (var (_, mapUid) in network.Comp.ZLevels)
        {
            if (!TryComp<MapComponent>(mapUid, out var mapComp))
            {
                Log.Error($"Map entity {mapUid} does not have MapComponent.");
                continue;
            }

            if (!_map.MapExists(mapComp.MapId))
            {
                Log.Error($"Map with ID {mapComp.MapId} does not exist.");
                continue;
            }

            if (_map.IsInitialized(mapComp.MapId))
            {
                Log.Debug($"Map with ID {mapComp.MapId} is already initialized.");
                continue;
            }

            _map.InitializeMap(mapComp.MapId);
            Log.Info($"Map with ID {mapComp.MapId} initialized.");
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateView(frameTime);
    }
}
