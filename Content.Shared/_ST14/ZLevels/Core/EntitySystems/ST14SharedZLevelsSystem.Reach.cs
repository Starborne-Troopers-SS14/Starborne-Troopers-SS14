/*
 * Starborne Troopers z-levels: reaching across an opening.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Damage.Components;
using Robust.Shared.Map;

namespace Content.Shared._ST14.ZLevels.Core.EntitySystems;

/// <summary>
/// ST14: a body on the edge of a trench reaches whoever is down in it, and the other way round: they see each other
/// through the opening, so a melee blow goes through it. Everywhere else the levels stay apart.
/// </summary>
public abstract partial class ST14SharedZLevelsSystem
{
    /// <summary>
    /// How close to the cursor a body down in an opening must be to be the one clicked
    /// </summary>
    private const float ReachPickRadius = 0.5f;

    private readonly HashSet<Entity<DamageableComponent>> _reachPick = new();

    /// <summary>
    /// <paramref name="user"/> and <paramref name="target"/> are on neighbouring levels, within
    /// <paramref name="range"/> of each other, and the one below stands under an opening in the level above: no floor,
    /// no roof over them
    /// </summary>
    public bool InReachAcrossLevels(EntityUid user, EntityUid target, float range)
    {
        var userXform = Transform(user);
        var targetXform = Transform(target);
        if (userXform.MapUid is not { } userMap || targetXform.MapUid is not { } targetMap || userMap == targetMap ||
            !_zMapQuery.HasComp(userMap) || !_zMapQuery.HasComp(targetMap))
        {
            return false;
        }

        EntityUid lower;
        EntityUid upperLevel;
        if (TryMapOffset(userMap, -1, out var below) && below.Owner == targetMap)
        {
            lower = target;
            upperLevel = userMap;
        }
        else if (TryMapOffset(userMap, 1, out var above) && above.Owner == targetMap)
        {
            lower = user;
            upperLevel = targetMap;
        }
        else
        {
            return false;
        }

        var userPos = _transform.GetWorldPosition(userXform);
        var targetPos = _transform.GetWorldPosition(targetXform);
        if ((userPos - targetPos).Length() > range)
            return false;

        return IsOpening(upperLevel, _transform.GetWorldPosition(lower));
    }

    /// <summary>
    /// Whoever can be hit down in the opening under <paramref name="point"/> (a point of an upper level): the body nearest
    /// it on the level below, if the level has no floor or roof there
    /// </summary>
    public EntityUid? EntityBelowThrough(MapCoordinates point)
    {
        if (!_map.TryGetMap(point.MapId, out var upperLevel) || upperLevel is not { } upper ||
            !IsOpening(upper, point.Position) || !TryMapOffset(upper, -1, out var below))
        {
            return null;
        }

        _reachPick.Clear();
        _lookup.GetEntitiesInRange(new MapCoordinates(point.Position, Transform(below).MapID), ReachPickRadius, _reachPick);

        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var candidate in _reachPick)
        {
            // walls and the like stand anchored; whoever is in the trench does not
            var xform = Transform(candidate);
            if (xform.Anchored)
                continue;

            var distance = (_transform.GetWorldPosition(xform) - point.Position).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate.Owner;
            }
        }

        return best;
    }

    /// <summary>
    /// Nothing on <paramref name="level"/> at this world point: no floor tile, no roof
    /// </summary>
    private bool IsOpening(EntityUid level, Vector2 worldPos)
    {
        var tile = new Vector2i((int) MathF.Floor(worldPos.X), (int) MathF.Floor(worldPos.Y));
        if (_solidSpots.Contains((level, tile)))
            return false;

        return !_map.TryFindGridAt(level, worldPos, out var gridUid, out var grid) ||
               !_map.TryGetTileRef(gridUid, grid, _map.WorldToTile(gridUid, grid, worldPos), out var tileRef) ||
               tileRef.Tile.IsEmpty;
    }
}
