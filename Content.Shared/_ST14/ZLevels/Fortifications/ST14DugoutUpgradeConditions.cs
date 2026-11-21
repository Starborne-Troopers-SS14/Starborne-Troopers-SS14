/*
 * Starborne Troopers bunker upgrades on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.ZLevels.Roof;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._ST14.ZLevels.Fortifications;

/// <summary>
/// Where the bunkers are, for their upgrades: which bunker (one 3x3 dug-out) a tile belongs to, what is built on it,
/// and which bunkers make one bunker structure with it
/// </summary>
public static class ST14Dugouts
{
    private static readonly Vector2i[] Sides = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];

    /// <summary>
    /// The bunker at <paramref name="location"/>: a point on its roof (the surface) or on its floor (the level under it)
    /// </summary>
    public static bool TryGetAt(IEntityManager entMan, EntityCoordinates location,
        out EntityUid surface, out Vector2i center, out int size)
    {
        surface = default;
        center = default;
        size = 0;

        var transform = entMan.System<SharedTransformSystem>();
        if (transform.GetMap(location) is not { } map)
            return false;

        var world = transform.ToMapCoordinates(location).Position;
        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));

        ST14DugoutPartComponent? part;
        if (TryGetPart(entMan, map, tile, out part))
        {
            surface = map;
        }
        else if (entMan.System<ST14SharedZLevelsSystem>().TryMapOffset(map, 1, out var above) &&
                 TryGetPart(entMan, above.Owner, tile, out part))
        {
            surface = above.Owner;
        }
        else
        {
            return false;
        }

        center = part.Center;
        size = part.Size;
        return true;
    }

    public static bool TryGetPart(IEntityManager entMan, EntityUid surface, Vector2i worldTile,
        [NotNullWhen(true)] out ST14DugoutPartComponent? part)
    {
        part = null;
        return entMan.System<ST14SharedZLevelsRoofSystem>().TryGetRoofEntity(surface, worldTile, out var roof) &&
               entMan.TryGetComponent(roof, out part);
    }

    public static IEnumerable<Vector2i> Tiles(Vector2i center, int size)
    {
        var half = size / 2;
        for (var x = -half; x <= half; x++)
        {
            for (var y = -half; y <= half; y++)
            {
                yield return center + new Vector2i(x, y);
            }
        }
    }

    /// <summary>
    /// A corner bunker: only part of its square is its own (the staircase on one side of the diagonal)
    /// </summary>
    public static bool IsCorner(IEntityManager entMan, EntityUid surface, Vector2i center, int size)
    {
        foreach (var tile in Tiles(center, size))
        {
            if (!TryGetPart(entMan, surface, tile, out var part) || part.Center != center)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The upgrade built (or being built) on the bunker: on its roof over the middle, or a blueprint there or on the
    /// floor under it
    /// </summary>
    public static EntityUid? UpgradeIn(IEntityManager entMan, EntityUid surface, Vector2i center, int size)
    {
        var lookup = entMan.System<EntityLookupSystem>();
        var middle = center + new Vector2(0.5f);
        var levels = new List<EntityUid> { surface };
        if (entMan.System<ST14SharedZLevelsSystem>().TryMapOffset(surface, -1, out var below))
            levels.Add(below.Owner);

        foreach (var level in levels)
        {
            var mapId = entMan.GetComponent<TransformComponent>(level).MapID;
            foreach (var uid in lookup.GetEntitiesInRange(new MapCoordinates(middle, mapId), 0.45f, LookupFlags.All))
            {
                if (entMan.HasComponent<ST14BunkerUpgradeComponent>(uid) || entMan.HasComponent<ST14BlueprintComponent>(uid))
                    return uid;
            }
        }

        return null;
    }

    /// <summary>
    /// Something is built (or being built) on a bunker built on to this one
    /// </summary>
    public static bool UpgradeNextTo(IEntityManager entMan, EntityUid surface, Vector2i center, int size)
    {
        var half = size / 2;
        var checkedParts = new HashSet<Vector2i> { center };
        for (var i = -half; i <= half; i++)
        {
            foreach (var tile in new[]
                     {
                         center + new Vector2i(i, half + 1), center + new Vector2i(i, -half - 1),
                         center + new Vector2i(half + 1, i), center + new Vector2i(-half - 1, i),
                     })
            {
                if (TryGetPart(entMan, surface, tile, out var part) && checkedParts.Add(part.Center) &&
                    UpgradeIn(entMan, surface, part.Center, part.Size) != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The middles of the bunkers that make one bunker structure with this one: built on to each other
    /// </summary>
    public static HashSet<Vector2i> Bunker(IEntityManager entMan, EntityUid surface, Vector2i start)
    {
        var parts = new HashSet<Vector2i>();
        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<Vector2i>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var tile) && seen.Count < 2000)
        {
            if (!TryGetPart(entMan, surface, tile, out var part))
                continue;

            parts.Add(part.Center);
            foreach (var side in Sides)
            {
                var next = tile + side;
                if (seen.Add(next) && TryGetPart(entMan, surface, next, out _))
                    queue.Enqueue(next);
            }
        }

        return parts;
    }

    /// <summary>
    /// The ground of <paramref name="surface"/> is there: not dug, not under a bunker
    /// </summary>
    public static bool IsGround(IEntityManager entMan, EntityUid surface, Vector2i worldTile)
    {
        if (TryGetPart(entMan, surface, worldTile, out _) ||
            !entMan.System<ST14SharedZLevelsSystem>().TryGetLayerGrid(surface, out var grid))
        {
            return false;
        }

        var map = entMan.System<SharedMapSystem>();
        var index = map.WorldToTile(grid.Value, grid.Value.Comp, worldTile + new Vector2(0.5f));
        return map.TryGetTileRef(grid.Value, grid.Value.Comp, index, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    /// <summary>
    /// The trench at <paramref name="location"/>: a tile dug out of the surface (not under a bunker), with its floor on
    /// the level below. The point can be on the surface over it or down on its floor.
    /// </summary>
    public static bool TryGetTrench(IEntityManager entMan, EntityCoordinates location,
        out EntityUid surface, out EntityUid floor, out Vector2i worldTile)
    {
        surface = default;
        floor = default;
        worldTile = default;

        var transform = entMan.System<SharedTransformSystem>();
        var zLevels = entMan.System<ST14SharedZLevelsSystem>();
        if (transform.GetMap(location) is not { } map || !entMan.TryGetComponent<ST14ZMapComponent>(map, out var zMap))
            return false;

        if (zMap.Depth == 0 && zLevels.TryMapOffset(map, -1, out var below))
        {
            surface = map;
            floor = below.Owner;
        }
        else if (zMap.Depth == -1 && zLevels.TryMapOffset(map, 1, out var above))
        {
            surface = above.Owner;
            floor = map;
        }
        else
        {
            return false;
        }

        var world = transform.ToMapCoordinates(location).Position;
        worldTile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (TryGetPart(entMan, surface, worldTile, out _) || IsGround(entMan, surface, worldTile) ||
            !zLevels.TryGetLayerGrid(surface, out _) || !zLevels.TryGetLayerGrid(floor, out var floorGrid))
        {
            return false;
        }

        var maps = entMan.System<SharedMapSystem>();
        var index = maps.WorldToTile(floorGrid.Value, floorGrid.Value.Comp, worldTile + new Vector2(0.5f));
        return maps.TryGetTileRef(floorGrid.Value, floorGrid.Value.Comp, index, out var floorRef) && !floorRef.Tile.IsEmpty;
    }
}

/// <summary>
/// A bunker upgrade goes on top of a bunker, over its middle: one a bunker. The engineer points at the bunker from inside
/// or from its roof. A forward base also wants a bunker structure of at least <see cref="MinParts"/> bunkers and no other
/// base near; a ramp wants open ground past the side it faces.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14DugoutUpgradeCondition : IConstructionCondition, IST14SnapCondition
{
    /// <summary>
    /// A forward base: the bunker structure must be big enough and no other base near it
    /// </summary>
    [DataField]
    public bool Base;

    [DataField]
    public int MinParts = 6;

    /// <summary>
    /// No other upgrade on a bunker built on to this one (the base and the observation bunker, as in Foxhole)
    /// </summary>
    [DataField]
    public bool Apart;

    /// <summary>
    /// A ramp: the ground just past the middle of the side it faces must be open, it goes down from there
    /// </summary>
    [DataField]
    public bool Ramp;

    /// <summary>
    /// How far apart two forward bases must be, in tiles
    /// </summary>
    [DataField]
    public float BaseSpacing = 20f;

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        return Error(location, direction) == null;
    }

    /// <summary>
    /// What is wrong with building here, a locale id, or null
    /// </summary>
    public string? Error(EntityCoordinates location, Direction direction = Direction.South)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!ST14Dugouts.TryGetAt(entMan, location, out var surface, out var center, out var size))
            return "st14-bunker-upgrade-not-in-dugout";

        // a corner bunker carries no upgrade: it only joins the others
        if (ST14Dugouts.IsCorner(entMan, surface, center, size))
            return "st14-bunker-upgrade-corner";

        if (ST14Dugouts.UpgradeIn(entMan, surface, center, size) != null)
            return "st14-bunker-upgrade-taken";

        if (Apart && ST14Dugouts.UpgradeNextTo(entMan, surface, center, size))
            return "st14-bunker-upgrade-not-apart";

        if (Ramp && !ST14Dugouts.IsGround(entMan, surface, center + direction.ToIntVec() * (size / 2 + 1)))
            return "st14-bunker-upgrade-ramp-blocked";

        if (!Base)
            return null;

        if (ST14Dugouts.Bunker(entMan, surface, center).Count < MinParts)
            return "st14-bunker-upgrade-too-small";

        var transform = entMan.System<SharedTransformSystem>();
        entMan.System<ST14SharedZLevelsSystem>().TryMapOffset(surface, -1, out var floor);
        var here = center + new Vector2(0.5f);
        var query = entMan.EntityQueryEnumerator<ST14BunkerBaseComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == floor.Owner && (transform.GetWorldPosition(xform) - here).Length() < BaseSpacing)
                return "st14-bunker-upgrade-base-near";
        }

        return null;
    }

    /// <summary>
    /// The blueprint goes in the middle of the bunker, on the level the engineer points at
    /// </summary>
    public bool TrySnap(EntityCoordinates location, Direction direction, out EntityCoordinates spot)
    {
        spot = location;
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!ST14Dugouts.TryGetAt(entMan, location, out _, out var center, out _) ||
            entMan.System<SharedTransformSystem>().GetMap(location) is not { } map)
        {
            return false;
        }

        spot = new EntityCoordinates(map, center + new Vector2(0.5f));
        return true;
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = Base ? "st14-construction-condition-bunker-base"
                : Ramp ? "st14-construction-condition-dugout-ramp"
                : Apart ? "st14-construction-condition-dugout-apart"
                : "st14-construction-condition-dugout",
        };
    }
}

/// <summary>
/// A firing slit goes into the side of a bunker, in the middle of it: where a door could go, in the earth wall there
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14DugoutSlitCondition : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var transform = entMan.System<SharedTransformSystem>();
        if (transform.GetMap(location) is not { } floorMap ||
            !entMan.System<ST14SharedZLevelsSystem>().TryMapOffset(floorMap, 1, out var above))
        {
            return false;
        }

        var world = transform.ToMapCoordinates(location).Position;
        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));

        // the middle of a side of a bunker next to it, and it is not a bunker itself
        if (ST14Dugouts.TryGetPart(entMan, above.Owner, tile, out _))
            return false;

        foreach (var side in new[] { new Vector2i(0, 1), new Vector2i(0, -1), new Vector2i(1, 0), new Vector2i(-1, 0) })
        {
            if (!ST14Dugouts.TryGetPart(entMan, above.Owner, tile + side, out var part))
                continue;

            foreach (var spot in ST14DugoutDoors.Spots(part.Center, part.Size))
            {
                if (spot == tile)
                    return true;
            }
        }

        return false;
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry { Localization = "st14-construction-condition-dugout-slit" };
    }
}

/// <summary>
/// A ladder goes inside a bunker, on its floor, but not in the middle (an upgrade may want it) nor where something
/// stands already
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14DugoutLadderCondition : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var transform = entMan.System<SharedTransformSystem>();
        if (transform.GetMap(location) is not { } floorMap ||
            !entMan.System<ST14SharedZLevelsSystem>().TryMapOffset(floorMap, 1, out var above))
        {
            return false;
        }

        var world = transform.ToMapCoordinates(location).Position;
        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (!ST14Dugouts.TryGetPart(entMan, above.Owner, tile, out var part) || tile == part.Center)
            return false;

        var lookup = entMan.System<EntityLookupSystem>();
        var mapId = entMan.GetComponent<TransformComponent>(floorMap).MapID;
        foreach (var uid in lookup.GetEntitiesInRange(new MapCoordinates(tile + new Vector2(0.5f), mapId), 0.45f, LookupFlags.Static | LookupFlags.Sundries))
        {
            if (entMan.HasComponent<ST14BunkerPassageComponent>(uid) || entMan.HasComponent<ST14BunkerLadderComponent>(uid))
                return false;
        }

        return true;
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry { Localization = "st14-construction-condition-dugout-ladder" };
    }
}

/// <summary>
/// A trench modification (barbed wire, a barricade) goes on a tile of a trench, one a tile. The engineer points at it
/// from the surface over the trench or from down in it.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14TrenchModCondition : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!ST14Dugouts.TryGetTrench(entMan, location, out _, out var floor, out var worldTile) ||
            !entMan.System<ST14SharedZLevelsSystem>().TryGetLayerGrid(floor, out var grid))
        {
            return false;
        }

        var maps = entMan.System<SharedMapSystem>();
        var index = maps.WorldToTile(grid.Value, grid.Value.Comp, worldTile + new Vector2(0.5f));
        var anchored = maps.GetAnchoredEntitiesEnumerator(grid.Value, grid.Value.Comp, index);
        while (anchored.MoveNext(out var uid))
        {
            if (entMan.HasComponent<ST14TrenchModComponent>(uid))
                return false;
        }

        return true;
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry { Localization = "st14-construction-condition-trench-mod" };
    }
}
