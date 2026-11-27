/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._ST14.ZLevels.Core.Components;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.ZLevels.Core;

public sealed partial class ST14ZLevelsSystem
{
    /// <summary>
    /// Creates a new zLevel Map Network entity
    /// </summary>
    [PublicAPI]
    public Entity<ST14ZMapNetworkComponent> CreateMapNetwork(ComponentRegistry? components = null)
    {
        var ent = Spawn();

        var zLevel = EnsureComp<ST14ZMapNetworkComponent>(ent);

        // Network entity must always reach clients no matter how far the viewer is from it
        _pvs.AddGlobalOverride(ent);

        zLevel.Components = components ?? new ComponentRegistry();

        return (ent, zLevel);
    }

    /// <summary>
    /// Attempts to add the specified maps to the zNetwork network at the specified depths
    /// </summary>
    [PublicAPI]
    public bool TryAddMapsIntoNetwork(Entity<ST14ZMapNetworkComponent> network, Dictionary<EntityUid, int> maps)
    {
        var success = true;
        var addedAny = false;

        foreach (var (mapUid, depth) in maps)
        {
            if (TryGetMapNetwork(mapUid, out var otherNetwork))
            {
                Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network}: This map is already in another network {otherNetwork}.");
                success = false;
                continue;
            }

            if (network.Comp.ZLevels.ContainsKey(depth))
            {
                Log.Error($"Failed to add map {mapUid} to ZLevelNetwork {network}: This depth is already occupied.");
                success = false;
                continue;
            }

            if (network.Comp.ZLevels.ContainsValue(mapUid))
            {
                Log.Error($"Failed attempt to add map {mapUid} to ZLevelNetwork {network} at depth {depth}: This map is already in this network.");
                success = false;
                continue;
            }

            network.Comp.ZLevels[depth] = mapUid;
            network.Comp.ZLevelByEntity[mapUid] = depth;

            var mapComponent = EnsureComp<ST14ZMapComponent>(mapUid);
            mapComponent.Depth = depth;
            mapComponent.NetworkUid = network;

            // A neighbour processed earlier in this same call already has its ST14ZMapComponent, so its
            // reverse pointer is set here; a neighbour processed later will set it the other way round
            // when its own turn comes — either order ends up with both pointers correct.
            if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveMapUid) && aboveMapUid is { } aboveUid)
            {
                mapComponent.MapAbove = aboveUid;

                if (TryComp<ST14ZMapComponent>(aboveUid, out var aboveMapComponent))
                {
                    aboveMapComponent.MapBelow = mapUid;
                    Dirty(aboveUid, aboveMapComponent);
                }
            }

            if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowMapUid) && belowMapUid is { } belowUid)
            {
                mapComponent.MapBelow = belowUid;

                if (TryComp<ST14ZMapComponent>(belowUid, out var belowMapComponent))
                {
                    belowMapComponent.MapAbove = mapUid;
                    Dirty(belowUid, belowMapComponent);
                }
            }

            Dirty(mapUid, mapComponent);
            addedAny = true;

            var ev = new ST14MapAddedIntoZNetworkEvent(network, depth);
            RaiseLocalEvent(mapUid, ref ev);
        }

        if (!addedAny)
            return success;

        RebuildSortedCache(network);

        InvalidateGroundCache(); // ST14: neighbours changed, cached ground heights may be stale
        RaiseLocalEvent(network.Owner, new ST14ZLevelMapNetworkUpdatedEvent(network.Owner), broadcast: true);

        return success;
    }

    /// <summary>
    /// Attempts to detach the specified maps from the zNetwork, freeing their depths for reuse and
    /// removing <see cref="ST14ZMapComponent"/> so nothing traverses into them any more.
    /// Does not delete the map entities themselves.
    /// </summary>
    [PublicAPI]
    public bool TryRemoveMapsFromNetwork(Entity<ST14ZMapNetworkComponent> network, IReadOnlyCollection<EntityUid> maps)
    {
        var success = true;
        var removed = new List<(EntityUid Map, int Depth)>(maps.Count);

        foreach (var mapUid in maps)
        {
            if (!network.Comp.ZLevelByEntity.TryGetValue(mapUid, out var depth))
            {
                Log.Error($"Failed attempt to remove map {mapUid} from ZLevelNetwork {network}: This map is not in this network.");
                success = false;
                continue;
            }

            network.Comp.ZLevels.Remove(depth);
            network.Comp.ZLevelByEntity.Remove(mapUid);

            removed.Add((mapUid, depth));
        }

        if (removed.Count == 0)
            return success;

        // Unlink only once every slot is cleared, otherwise removing a contiguous block of maps would
        // leave a survivor pointing at a map that is also on its way out.
        // A hole in the network stays a real hole: traversal stops at it instead of silently
        // skipping over to the next occupied depth.
        foreach (var (mapUid, depth) in removed)
        {
            if (network.Comp.ZLevels.TryGetValue(depth + 1, out var aboveMapUid) &&
                aboveMapUid is { } aboveUid &&
                TryComp<ST14ZMapComponent>(aboveUid, out var aboveMapComponent))
            {
                aboveMapComponent.MapBelow = null;
                Dirty(aboveUid, aboveMapComponent);
            }

            if (network.Comp.ZLevels.TryGetValue(depth - 1, out var belowMapUid) &&
                belowMapUid is { } belowUid &&
                TryComp<ST14ZMapComponent>(belowUid, out var belowMapComponent))
            {
                belowMapComponent.MapAbove = null;
                Dirty(belowUid, belowMapComponent);
            }

            if (!TerminatingOrDeleted(mapUid))
                RemComp<ST14ZMapComponent>(mapUid);
        }

        RebuildSortedCache(network);

        foreach (var (mapUid, depth) in removed)
        {
            var ev = new ST14MapRemovedFromZNetworkEvent(network, depth);
            RaiseLocalEvent(mapUid, ref ev);
        }

        InvalidateGroundCache(); // ST14: neighbours changed, cached ground heights may be stale
        RaiseLocalEvent(network.Owner, new ST14ZLevelMapNetworkUpdatedEvent(network.Owner), broadcast: true);

        return success;
    }

    /// <summary>
    /// Returns the map entity at a specific depth within a z-network, or false if none exists.
    /// </summary>
    [PublicAPI]
    public bool TryGetMapAtDepth(Entity<ST14ZMapNetworkComponent?> network, int depth, out EntityUid mapUid)
    {
        mapUid = EntityUid.Invalid;

        if (!Resolve(network, ref network.Comp, false) ||
            !network.Comp.ZLevels.TryGetValue(depth, out var uid) ||
            uid is not { } validUid)
            return false;

        mapUid = validUid;
        return true;
    }

    /// <summary>
    /// Deletes a map z-network: queues deletion of all maps in the network, then the network entity itself.
    /// </summary>
    [PublicAPI]
    public void DeleteMapNetwork(EntityUid networkUid)
    {
        if (!TryComp<ST14ZMapNetworkComponent>(networkUid, out var zNet))
        {
            Log.Error($"ST14ZLevelsSystem: entity {networkUid} does not have ST14ZLevelsNetworkComponent.");
            return;
        }

        foreach (var (_, mapUid) in zNet.ZLevels)
        {
            if (mapUid != null)
                QueueDel(mapUid.Value);
        }

        QueueDel(networkUid);
    }

    /// <summary>
    /// Rebuilds the dense depth-indexed lookup from <see cref="ST14ZMapNetworkComponent.ZLevels"/>.
    /// A network holds a handful of maps, so a full rebuild on every composition change is cheap —
    /// and it is what keeps the cache honest when maps are removed instead of only ever appended.
    /// </summary>
    private void RebuildSortedCache(Entity<ST14ZMapNetworkComponent> network)
    {
        var comp = network.Comp;
        var list = comp.SortedZLevels;

        list.Clear();

        var min = int.MaxValue;
        var max = int.MinValue;

        foreach (var (depth, mapUid) in comp.ZLevels)
        {
            if (mapUid is not { } uid || !uid.IsValid())
                continue;

            if (depth < min)
                min = depth;

            if (depth > max)
                max = depth;
        }

        // No occupied depths left — collapse to the empty state instead of keeping stale bounds.
        if (min > max)
        {
            comp.SortedMin = 0;
            comp.SortedMax = 0;
            Dirty(network);
            return;
        }

        for (var depth = min; depth <= max; depth++)
        {
            list.Add(comp.ZLevels.TryGetValue(depth, out var mapUid) && mapUid is { } uid && uid.IsValid()
                ? uid
                : EntityUid.Invalid);
        }

        comp.SortedMin = min;
        comp.SortedMax = max;

        Dirty(network);
    }
}

/// <summary>
/// Called on ZLevel Network Entity, when maps added or removed from network.
/// </summary>
public sealed class ST14ZLevelMapNetworkUpdatedEvent(EntityUid network) : EntityEventArgs
{
    public readonly EntityUid Network = network;
}

/// <summary>
/// Called on map, when it added to ZNetwork
/// </summary>
[ByRefEvent]
public readonly struct ST14MapAddedIntoZNetworkEvent(Entity<ST14ZMapNetworkComponent> network, int depth)
{
    public readonly Entity<ST14ZMapNetworkComponent> Network = network;
    public readonly int Depth = depth;
}

/// <summary>
/// Called on map, when it is detached from a ZNetwork. The map entity itself is not deleted by this.
/// </summary>
[ByRefEvent]
public readonly struct ST14MapRemovedFromZNetworkEvent(Entity<ST14ZMapNetworkComponent> network, int depth)
{
    public readonly Entity<ST14ZMapNetworkComponent> Network = network;
    public readonly int Depth = depth;
}