/*
 * Starborne Troopers bunker roofs on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;

namespace Content.Shared._ST14.ZLevels.Roof;

/// <summary>
/// Roof entities (<see cref="ST14BunkerRoofComponent"/>) by level and world tile. They stand on an open tile of the
/// level above a bunker, so the roof is looked up by where it stands, not by the tile under it. A roof is ground to walk
/// on (ST14SharedZLevelsSystem.SetSolidSpot): whoever climbs onto a bunker walks over its roof.
/// </summary>
public abstract partial class ST14SharedZLevelsRoofSystem
{
    private readonly Dictionary<EntityUid, Dictionary<Vector2i, EntityUid>> _roofEntities = new();

    [SubscribeLocalEvent]
    private void OnRoofStartup(Entity<ST14BunkerRoofComponent> ent, ref ComponentStartup args)
    {
        if (!TryGetRoofSpot(ent, out var map, out var worldTile))
            return;

        if (!_roofEntities.TryGetValue(map, out var tiles))
        {
            tiles = new Dictionary<Vector2i, EntityUid>();
            _roofEntities[map] = tiles;
        }

        tiles[worldTile] = ent;
        ZLevel.SetSolidSpot(map, worldTile, true);
        OnRoofAdded(map, worldTile);
    }

    [SubscribeLocalEvent]
    private void OnRoofShutdown(Entity<ST14BunkerRoofComponent> ent, ref ComponentShutdown args)
    {
        foreach (var (map, tiles) in _roofEntities)
        {
            foreach (var (tile, uid) in tiles)
            {
                if (uid != ent.Owner)
                    continue;

                tiles.Remove(tile);
                ZLevel.SetSolidSpot(map, tile, false);
                if (tiles.Count == 0)
                    _roofEntities.Remove(map);

                OnRoofRemoved(map, tile);
                return;
            }
        }
    }

    /// <summary>
    /// The roof entity standing on <paramref name="worldTile"/> of the level <paramref name="map"/>
    /// </summary>
    public bool TryGetRoofEntity(EntityUid map, Vector2i worldTile, [NotNullWhen(true)] out EntityUid? roof)
    {
        roof = null;
        if (!_roofEntities.TryGetValue(map, out var tiles) || !tiles.TryGetValue(worldTile, out var uid))
            return false;

        roof = uid;
        return true;
    }

    /// <summary>
    /// Every roof entity of a level by world tile, null when it has none
    /// </summary>
    public IReadOnlyDictionary<Vector2i, EntityUid>? RoofEntitiesOn(EntityUid map)
    {
        return _roofEntities.GetValueOrDefault(map);
    }

    protected virtual void OnRoofAdded(EntityUid map, Vector2i worldTile)
    {
    }

    protected virtual void OnRoofRemoved(EntityUid map, Vector2i worldTile)
    {
    }

    private bool TryGetRoofSpot(EntityUid uid, out EntityUid map, out Vector2i worldTile)
    {
        map = default;
        worldTile = default;
        var xform = XformQuery.GetComponent(uid);
        if (xform.MapUid is not { } mapUid)
            return false;

        map = mapUid;
        var world = XformSystem.GetWorldPosition(xform);
        worldTile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        return true;
    }
}
