/*
 * Starborne Troopers z-level layers. Part of the z-levels based on CrystallEdge (MIT License),
 * see LICENSE.TXT in the ZLevels folder.
 */

using System.Numerics;
using Content.Server._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Layers;
using Content.Shared._ST14.ZLevels.Roof;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.ZLevels.Layers;

/// <summary>
/// Builds the levels around a map from an <see cref="ST14ZLayerSetPrototype"/> and links them into one z-network
/// with it. The main map stays depth 0. A filled level gets one ground tile under the whole footprint of the main
/// map (tiles, not entities, so a 200x200 sector costs no entities beyond its edge ring); an empty level is open
/// air on a planet map (the map entity is its grid), ready for roofs and flyers.
/// </summary>
public sealed partial class ST14ZLayerSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;

    /// <summary>
    /// Makes sure the main map has every level of the set. Levels already in <paramref name="existing"/> (loaded from a
    /// save) are kept and only linked; missing ones are built fresh. Returns every level by depth, 0 included
    /// </summary>
    public Dictionary<int, EntityUid> EnsureLayers(
        EntityUid mainMap,
        ProtoId<ST14ZLayerSetPrototype> setId,
        IReadOnlyDictionary<int, EntityUid>? existing = null)
    {
        var levels = new Dictionary<int, EntityUid> { [0] = mainMap };
        if (existing != null)
        {
            foreach (var (depth, map) in existing)
            {
                if (!TerminatingOrDeleted(map))
                    levels[depth] = map;
            }
        }

        if (!_prototype.TryIndex(setId, out var set))
        {
            Log.Error($"Unknown z-layer set {setId}");
            return levels;
        }

        Footprint? footprint = null;
        foreach (var layer in set.Layers)
        {
            if (layer.Depth == 0 || levels.ContainsKey(layer.Depth))
                continue;

            if (layer.Fill != null)
                footprint ??= ComputeFootprint(mainMap, set);

            levels[layer.Depth] = CreateLayer(mainMap, layer, footprint);
        }

        Link(mainMap, levels);
        return levels;
    }

    private sealed class Footprint
    {
        /// <summary>World tiles of open ground on the main map</summary>
        public readonly HashSet<Vector2i> Ground = new();

        /// <summary>Innermost ring of the edge of the main map, next to open ground</summary>
        public readonly HashSet<Vector2i> Ring = new();
    }

    private Footprint ComputeFootprint(EntityUid mainMap, ST14ZLayerSetPrototype set)
    {
        var footprint = new Footprint();
        if (!_zLevels.TryGetLayerGrid(mainMap, out var main))
            return footprint;

        var boundary = new HashSet<Vector2i>();
        if (set.BoundaryComponent is { } name && Factory.TryGetRegistration(name, out var registration))
        {
            foreach (var (uid, _) in EntityManager.GetAllComponents(registration.Type))
            {
                var xform = Transform(uid);
                if (xform.MapUid != mainMap)
                    continue;

                var world = _transform.GetWorldPosition(xform);
                boundary.Add(new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y)));
            }
        }

        var tiles = _map.GetAllTilesEnumerator(main.Value, main.Value.Comp);
        while (tiles.MoveNext(out var tile))
        {
            var world = _zLevels.GridTileToWorldTile(main.Value, main.Value.Comp, tile.Value.GridIndices);
            if (!boundary.Contains(world))
                footprint.Ground.Add(world);
        }

        foreach (var edge in boundary)
        {
            if (footprint.Ground.Contains(edge + Vector2i.Up) || footprint.Ground.Contains(edge + Vector2i.Down) ||
                footprint.Ground.Contains(edge + Vector2i.Left) || footprint.Ground.Contains(edge + Vector2i.Right))
            {
                footprint.Ring.Add(edge);
            }
        }

        return footprint;
    }

    private EntityUid CreateLayer(EntityUid mainMap, ST14ZLayer layer, Footprint? footprint)
    {
        var map = _map.CreateMap(out _);
        var name = layer.Name is { } loc ? Loc.GetString(loc) : string.Empty;
        _meta.SetEntityName(map, $"{MetaData(mainMap).EntityName} [{layer.Depth:+0;-0;0}] {name}".TrimEnd());

        // a planet map: the map is its own grid, it lines up with the main map by world tile
        var grid = EnsureComp<MapGridComponent>(map);

        // the same sky over every level; roofs (the levels above) keep it off what is covered
        if (TryComp<MapLightComponent>(mainMap, out var mainLight))
        {
            var light = EnsureComp<MapLightComponent>(map);
            light.AmbientLightColor = mainLight.AmbientLightColor;
            Dirty(map, light);
        }

        CopyGravity(mainMap, map);

        if (layer.Fill is { } fill && footprint != null)
        {
            var tile = new Tile(_tiles[fill].TileId);
            var list = new List<(Vector2i, Tile)>(footprint.Ground.Count + footprint.Ring.Count);
            foreach (var world in footprint.Ground)
            {
                list.Add((world, tile));
            }

            foreach (var world in footprint.Ring)
            {
                list.Add((world, tile));
            }

            _map.SetTiles(map, grid, list);

            if (layer.Edge is { } edge)
            {
                foreach (var world in footprint.Ring)
                {
                    Spawn(edge, new EntityCoordinates(map, world + new Vector2(0.5f)));
                }
            }
        }

        return map;
    }

    private void CopyGravity(EntityUid from, EntityUid to)
    {
        if (!TryComp<GravityComponent>(from, out var source) || !source.Enabled)
            return;

        var gravity = EnsureComp<GravityComponent>(to);
        gravity.Inherent = source.Inherent;
        gravity.Enabled = true;
        var ev = new GravityChangedEvent(to, true);
        RaiseLocalEvent(to, ref ev, true);
        Dirty(to, gravity);
    }

    /// <summary>
    /// Puts every level into the main map's network (making one if it has none), and marks every level's ground
    /// for roof updates, so a floor dug out or a roof built changes the roofs of the levels under it
    /// </summary>
    private void Link(EntityUid mainMap, Dictionary<int, EntityUid> levels)
    {
        if (!_zLevels.TryGetMapNetwork(mainMap, out var network))
        {
            network = _zLevels.CreateMapNetwork();
            _meta.SetEntityName(network, $"Z-network: {MetaData(mainMap).EntityName}");
        }

        var missing = new Dictionary<EntityUid, int>();
        foreach (var (depth, map) in levels)
        {
            if (!network.Comp.ZLevelByEntity.ContainsKey(map))
                missing[map] = depth;
        }

        // roof markers first: adding the maps recalculates the roofs of the whole network
        foreach (var map in levels.Values)
        {
            if (_zLevels.TryGetLayerGrid(map, out var grid))
                EnsureComp<ST14ZLevelRoofComponent>(grid.Value);
        }

        if (missing.Count > 0)
            _zLevels.TryAddMapsIntoNetwork(network, missing);
    }

    /// <summary>
    /// Every level of the network the map is in, by depth; just the map at depth 0 when it has none
    /// </summary>
    public Dictionary<int, EntityUid> GetLevels(EntityUid map)
    {
        var levels = new Dictionary<int, EntityUid>();
        if (_zLevels.TryGetMapNetwork(map, out var network))
        {
            foreach (var (depth, uid) in network.Comp.ZLevels)
            {
                if (uid is { } level)
                    levels[depth] = level;
            }
        }

        if (levels.Count == 0)
            levels[0] = map;

        return levels;
    }
}
