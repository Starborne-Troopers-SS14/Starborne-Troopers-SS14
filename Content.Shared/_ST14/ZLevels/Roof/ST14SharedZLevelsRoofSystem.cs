/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._ST14.ZLevels.Roof;

/// <summary>
/// Keeps the vanilla <see cref="RoofComponent"/> of every level in sync with the levels above it: a tile is rooved
/// when an opaque tile lies over it on any level above. The roof keeps the sky light and the weather off the tile,
/// so the ground of a sector darkens the tunnels under it and a roof built on +1 covers the building under it.
/// ST14: works on layers matched by world tile, so a level whose ground is a separate grid on its map (a sector's
/// main level) and planet-map layers (the map is the grid) line up.
/// </summary>
public abstract partial class ST14SharedZLevelsRoofSystem : EntitySystem
{
    [Dependency] protected ST14SharedZLevelsSystem ZLevel = null!;
    [Dependency] protected SharedRoofSystem Roof = null!;
    [Dependency] protected SharedMapSystem Map = null!;
    [Dependency] protected ITileDefinitionManager TilDefMan = null!;
    [Dependency] protected SharedTransformSystem XformSystem = null!;

    [Dependency] protected EntityQuery<MapGridComponent> GridQuery = default!;
    [Dependency] protected EntityQuery<RoofComponent> RoofQuery = default!;
    [Dependency] protected EntityQuery<ST14ZMapComponent> ZMapQuery = default!;
    [Dependency] protected EntityQuery<TransformComponent> XformQuery = default!;

    private readonly Dictionary<Vector2i, bool> _changed = new();
    private readonly List<Vector2i> _keys = new();

    /// <summary>
    /// True when the tile shuts out what is above it: present and not see-through (glass is)
    /// </summary>
    protected bool IsOpaque(Tile tile)
    {
        return !tile.IsEmpty && !((ContentTileDefinition) TilDefMan[tile.TypeId]).Transparent;
    }

    [SubscribeLocalEvent]
    private void OnTileChanged(Entity<ST14ZLevelRoofComponent> ent, ref TileChangedEvent args)
    {
        if (args.Changes.Length == 0 || !GridQuery.TryComp(ent, out var grid))
            return;

        // the level is the map the grid stands on (or the map itself for a planet-map layer)
        if (XformQuery.GetComponent(ent).MapUid is not { } mapUid || !ZMapQuery.TryComp(mapUid, out var zMap))
            return;

        RoofQuery.TryComp(ent, out var currentRoof);

        _changed.Clear();
        foreach (var change in args.Changes)
        {
            var roovedAbove = currentRoof != null && Roof.IsRooved((ent, grid, currentRoof), change.GridIndices);
            var worldTile = ZLevel.GridTileToWorldTile(ent, grid, change.GridIndices);
            // ST14: a bunker roof standing on the tile covers it whatever the tile is
            _changed[worldTile] = roovedAbove || IsOpaque(change.NewTile) || TryGetRoofEntity(mapUid, worldTile, out _);
        }

        foreach (var mapBelow in ZLevel.GetAllMapsBelow((mapUid, zMap)))
        {
            if (!ZLevel.TryGetLayerGrid(mapBelow, out var below))
                continue;

            var roofBelow = EnsureComp<RoofComponent>(below.Value);
            _keys.Clear();
            _keys.AddRange(_changed.Keys);
            foreach (var worldTile in _keys)
            {
                var index = Map.WorldToTile(below.Value, below.Value.Comp, worldTile + new Vector2(0.5f));
                Roof.SetRoof((below.Value, below.Value.Comp, roofBelow), index, _changed[worldTile]);

                // an opaque tile on this level roofs everything further down whatever happens above it
                if (Map.TryGetTile(below.Value.Comp, index, out var tile) && IsOpaque(tile))
                    _changed[worldTile] = true;
            }
        }

        _changed.Clear();
    }
}
