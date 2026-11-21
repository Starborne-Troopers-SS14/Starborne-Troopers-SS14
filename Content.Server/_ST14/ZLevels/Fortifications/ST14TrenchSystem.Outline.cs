/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Outlines drawn over many tiles as one: the wooden rim on the ground around a trench and the string of a layout. Each
/// tile has an entity with a piece for every side and outer corner (<see cref="ST14OutlineVisuals"/>); only the pieces on
/// the edge of the shape are shown, so neighbouring tiles join into one frame.
/// </summary>
public sealed partial class ST14TrenchSystem
{
    public static readonly EntProtoId TrenchRim = "ST14TrenchRim";

    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;

    private static readonly (ST14OutlineVisuals Key, Vector2i Side)[] OutlineSides =
    [
        (ST14OutlineVisuals.North, new Vector2i(0, 1)),
        (ST14OutlineVisuals.South, new Vector2i(0, -1)),
        (ST14OutlineVisuals.East, new Vector2i(1, 0)),
        (ST14OutlineVisuals.West, new Vector2i(-1, 0)),
    ];

    private static readonly (ST14OutlineVisuals Key, Vector2i A, Vector2i B)[] OutlineInnerCorners =
    [
        (ST14OutlineVisuals.InnerNorthEast, new Vector2i(0, 1), new Vector2i(1, 0)),
        (ST14OutlineVisuals.InnerNorthWest, new Vector2i(0, 1), new Vector2i(-1, 0)),
        (ST14OutlineVisuals.InnerSouthEast, new Vector2i(0, -1), new Vector2i(1, 0)),
        (ST14OutlineVisuals.InnerSouthWest, new Vector2i(0, -1), new Vector2i(-1, 0)),
    ];

    private static readonly (ST14OutlineVisuals Key, Vector2i A, Vector2i B)[] OutlineCorners =
    [
        (ST14OutlineVisuals.NorthEast, new Vector2i(0, 1), new Vector2i(1, 0)),
        (ST14OutlineVisuals.NorthWest, new Vector2i(0, 1), new Vector2i(-1, 0)),
        (ST14OutlineVisuals.SouthEast, new Vector2i(0, -1), new Vector2i(1, 0)),
        (ST14OutlineVisuals.SouthWest, new Vector2i(0, -1), new Vector2i(-1, 0)),
    ];

    /// <summary>
    /// Shows the pieces of <paramref name="uid"/> on the edge of a shape: a side where the neighbour is not
    /// <paramref name="inside"/>, a corner where both its sides are and the tile across the corner is not inside either
    /// </summary>
    private void SetOutline(EntityUid uid, Vector2i tile, Func<Vector2i, bool> inside)
    {
        foreach (var (key, side) in OutlineSides)
        {
            _appearance.SetData(uid, key, !inside(tile + side));
        }

        foreach (var (key, a, b) in OutlineCorners)
        {
            _appearance.SetData(uid, key, !inside(tile + a) && !inside(tile + b) && !inside(tile + a + b));
        }

        // where the shape turns inward: the string of the two neighbours meets in this tile
        foreach (var (key, a, b) in OutlineInnerCorners)
        {
            _appearance.SetData(uid, key, inside(tile + a) && inside(tile + b) && !inside(tile + a + b));
        }
    }

    /// <summary>
    /// The wooden rim of a trench around <paramref name="worldTile"/> and the tiles next to it, put in step with what is
    /// dug: a dug tile of the surface gets a rim on its sides that border the ground
    /// </summary>
    private void UpdateRims(EntityUid ground, Vector2i worldTile)
    {
        if (!_zLevels.TryGetLayerGrid(ground, out var grid))
            return;

        var surface = grid.Value;
        bool Dug(Vector2i t) => !IsGround(surface, t);

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var tile = worldTile + new Vector2i(dx, dy);
                var rim = RimAt(ground, tile);

                // only a dug tile of the surface that a trench lies under has a rim
                if (!Dug(tile) || !HasTrenchFloor(ground, tile))
                {
                    if (rim != null)
                        QueueDel(rim.Value);

                    continue;
                }

                rim ??= Spawn(TrenchRim, new MapCoordinates(tile + new Vector2(0.5f), Transform(ground).MapID));
                SetOutline(rim.Value, tile, Dug);
            }
        }
    }

    private bool HasTrenchFloor(EntityUid ground, Vector2i worldTile)
    {
        if (!_zLevels.TryMapDown(ground, out var below) || !_zLevels.TryGetLayerGrid(below, out var floor))
            return false;

        return IsGround(floor.Value, worldTile);
    }

    private EntityUid? RimAt(EntityUid ground, Vector2i worldTile)
    {
        var coords = new MapCoordinates(worldTile + new Vector2(0.5f), Transform(ground).MapID);
        foreach (var rim in _lookup.GetEntitiesInRange<ST14TrenchRimComponent>(coords, 0.2f))
        {
            if (!TerminatingOrDeleted(rim))
                return rim;
        }

        return null;
    }

    /// <summary>
    /// The string of the layouts on <paramref name="map"/> goes round them all as one: round the whole segment, not
    /// round every tile of it, and two layouts side by side join up. <paramref name="removed"/> is a layout going away.
    /// </summary>
    private void RefreshBlueprintOutlines(EntityUid map, EntityUid? removed = null)
    {
        var tiles = new HashSet<Vector2i>();
        var query = EntityQueryEnumerator<ST14TrenchBlueprintComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var blueprint, out var xform))
        {
            if (uid != removed && xform.MapUid == map)
                tiles.UnionWith(blueprint.Tiles);
        }

        // a layout that comes up to something dug joins it: no string along a trench or a dug-out, its rim is the edge
        Entity<MapGridComponent>? surface = _zLevels.TryGetLayerGrid(map, out var grid) ? grid : null;
        bool Inside(Vector2i t) => tiles.Contains(t) || surface is { } s && _map.TryGetTileRef(s, s.Comp, TileOf(s, t), out var tileRef) && tileRef.Tile.IsEmpty;

        query = EntityQueryEnumerator<ST14TrenchBlueprintComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var blueprint, out var xform))
        {
            if (uid == removed || xform.MapUid != map)
                continue;

            for (var i = 0; i < blueprint.Markers.Count && i < blueprint.Tiles.Count; i++)
            {
                if (!TerminatingOrDeleted(blueprint.Markers[i]))
                    SetOutline(blueprint.Markers[i], blueprint.Tiles[i], Inside);
            }
        }
    }

    /// <summary>
    /// A dug-out roof shows its raised edges and its log side only round the whole roof, not between its tiles
    /// </summary>
    private void RefreshRoofOutlines(EntityUid level, IEnumerable<Vector2i> worldTiles)
    {
        var done = new HashSet<Vector2i>();
        foreach (var worldTile in worldTiles)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var tile = worldTile + new Vector2i(dx, dy);
                    if (!done.Add(tile) || !_roofs.TryGetRoofEntity(level, tile, out var roof) || !HasComp<AppearanceComponent>(roof))
                        continue;

                    // roofs side by side are one roof: dug-outs next to each other are one building
                    SetOutline(roof.Value, tile, t => _roofs.TryGetRoofEntity(level, t, out _));

                    // one ladder, leaning on the middle of the south side of the dug-out - where there is ground under it,
                    // not where another dug-out is built on
                    if (TryComp<ST14DugoutPartComponent>(roof, out var part))
                    {
                        var ladder = tile == part.Center - new Vector2i(0, part.Size / 2) &&
                                     !_roofs.TryGetRoofEntity(level, tile - new Vector2i(0, 1), out _);
                        _appearance.SetData(roof.Value, ST14DugoutVisuals.Ladder, ladder);
                    }
                }
            }
        }
    }
}
