/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Linq;
using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Dug-outs are closed all round: earth walls on every side, whatever is dug next to them. The only ways in are the
/// middle of each side - a trench dug up to it there is a door - so the soldiers choose where the doors go, at most
/// four, and a dug-out never turns into a sieve open on every side.
/// </summary>
public sealed partial class ST14TrenchSystem
{
    /// <summary>
    /// The door spots of a dug-out: the tile just outside the middle of each side
    /// </summary>
    public static IEnumerable<Vector2i> DoorSpots(Vector2i center, int size) => ST14DugoutDoors.Spots(center, size);

    /// <summary>
    /// Laying out <paramref name="tiles"/> keeps every dug-out closed but for its doors.
    /// <list type="bullet">
    /// <item>A trench may come up to a dug-out (dug or laid out) only at one of its door spots.</item>
    /// <item>A dug-out may be laid out next to dug ground or a layout only where that is one of its own door spots, or
    /// right next to another dug-out: the two are one building, open to each other.</item>
    /// </list>
    /// </summary>
    private bool DugoutWallsHold(EntityUid ground, Entity<MapGridComponent> surface, List<Vector2i> tiles, bool dugout)
    {
        var own = new HashSet<Vector2i>(tiles);
        if (dugout)
        {
            var center = CenterOf(tiles);
            var size = SizeOf(tiles);
            var doors = DoorSpots(center, size).ToHashSet();
            foreach (var tile in tiles)
            {
                foreach (var side in Cardinals)
                {
                    var next = tile + side;
                    if (own.Contains(next))
                        continue;

                    // wall to wall with another bunker is fine, but only flush: side to side, edge to edge (no
                    // staggered bunkers, see ST14TrenchShapes.SnappedBunkerStart)
                    if (DugoutAt(ground, next) is { } other)
                    {
                        var delta = other.Center - center;
                        var reach = (size + other.Size) / 2;
                        var flush = delta.Y == 0 && Math.Abs(delta.X) == reach || delta.X == 0 && Math.Abs(delta.Y) == reach;
                        if (!flush)
                            return false;

                        continue;
                    }

                    var open = !IsGround(surface, next) || BlueprintAt(ground, next) != null;
                    if (open && !doors.Contains(next))
                        return false;
                }
            }

            return true;
        }

        foreach (var tile in tiles)
        {
            foreach (var side in Cardinals)
            {
                if (DugoutAt(ground, tile + side) is not { } dugoutAt)
                    continue;

                if (!DoorSpots(dugoutAt.Center, dugoutAt.Size).Contains(tile))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The dug-out a tile of the surface belongs to, dug (its roof says so) or laid out
    /// </summary>
    public (Vector2i Center, int Size)? DugoutAt(EntityUid ground, Vector2i worldTile)
    {
        if (_roofs.TryGetRoofEntity(ground, worldTile, out var roof) &&
            TryComp<ST14DugoutPartComponent>(roof, out var part))
        {
            return (part.Center, part.Size);
        }

        if (BlueprintAt(ground, worldTile) is { } layout &&
            TryComp<ST14TrenchBlueprintComponent>(layout, out var blueprint) && blueprint.Roofed)
        {
            return (CenterOf(blueprint.Tiles), SizeOf(blueprint.Tiles));
        }

        return null;
    }

    /// <summary>
    /// Every roof of a dug-out just dug knows the dug-out it covers, so the walls of it hold later
    /// </summary>
    private void MarkDugout(EntityUid ground, List<Vector2i> tiles)
    {
        var center = CenterOf(tiles);
        var size = SizeOf(tiles);
        foreach (var tile in tiles)
        {
            if (!_roofs.TryGetRoofEntity(ground, tile, out var roof))
                continue;

            var part = EnsureComp<ST14DugoutPartComponent>(roof.Value);
            part.Center = center;
            part.Size = size;
            Dirty(roof.Value, part);
        }

        // a dug-out next to a trench: a partition along the edge they share, so nothing opens into it until a door is cut
        // there. Dug-outs side by side are one building: nothing between them
        var own = new HashSet<Vector2i>(tiles);
        var surface = _zLevels.TryGetLayerGrid(ground, out var grid) ? grid : null;
        foreach (var tile in tiles)
        {
            foreach (var side in Cardinals)
            {
                var next = tile + side;
                if (own.Contains(next))
                    continue;

                if (surface is { } s && !IsGround(s, next) && DugoutAt(ground, next) == null)
                    PlaceSeam(ground, tile, side);
            }
        }

        // the roofs of dug-outs side by side join into one
        RefreshRoofOutlines(ground, tiles);
    }

    public static readonly Robust.Shared.Prototypes.EntProtoId DugoutSeam = "ST14DugoutSeam";

    /// <summary>
    /// A plank partition on the floor of <paramref name="worldTile"/> along its <paramref name="side"/> edge, between
    /// two dug-outs side by side
    /// </summary>
    private void PlaceSeam(EntityUid ground, Vector2i worldTile, Vector2i side)
    {
        if (!_zLevels.TryMapDown(ground, out var below) || !_zLevels.TryGetLayerGrid(below, out var floor) ||
            SeamAt(floor.Value, worldTile, side) != null)
        {
            return;
        }

        // the partition is drawn and blocks along the south edge; turned, along the edge it faces
        var facing = new Vector2(side.X, side.Y).GetDir();
        var seam = Spawn(DugoutSeam, _map.GridTileToLocal(floor.Value, floor.Value.Comp, TileOf(floor.Value, worldTile)));
        _transform.SetLocalRotation(seam, facing.ToAngle());
    }

    /// <summary>
    /// The partition on <paramref name="worldTile"/> along its <paramref name="side"/> edge, if any
    /// </summary>
    private EntityUid? SeamAt(Entity<MapGridComponent> floor, Vector2i worldTile, Vector2i side)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(floor, floor.Comp, TileOf(floor, worldTile));
        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID == DugoutSeam.Id &&
                Transform(uid.Value).LocalRotation.GetCardinalDir().ToIntVec() == side)
            {
                return uid;
            }
        }

        return null;
    }

    /// <summary>
    /// The partition between two neighbouring tiles of dug-outs side by side, from whichever side it stands
    /// </summary>
    private EntityUid? SeamBetween(EntityUid ground, Vector2i a, Vector2i b)
    {
        if (!_zLevels.TryMapDown(ground, out var below) || !_zLevels.TryGetLayerGrid(below, out var floor))
            return null;

        return SeamAt(floor.Value, a, b - a) ?? SeamAt(floor.Value, b, a - b);
    }

    private static Vector2i CenterOf(IReadOnlyCollection<Vector2i> tiles)
    {
        var min = new Vector2i(tiles.Min(t => t.X), tiles.Min(t => t.Y));
        var max = new Vector2i(tiles.Max(t => t.X), tiles.Max(t => t.Y));
        return new Vector2i((min.X + max.X) / 2, (min.Y + max.Y) / 2);
    }

    private static int SizeOf(IReadOnlyCollection<Vector2i> tiles)
    {
        return tiles.Max(t => t.X) - tiles.Min(t => t.X) + 1;
    }

    /// <summary>
    /// The door of a dug-out, cut where the soldier points: the middle of its side nearest the cursor. It is laid out as
    /// one tile and dug like any segment, and opens the dug-out to whatever is dug outside it.
    /// </summary>
    private void CutDoor(Entity<ST14TrenchDiggerComponent> digger, EntityUid user, EntityUid ground, Vector2 cursor, Vector2 userPos)
    {
        if (!ST14DugoutDoors.TryFind(cursor, t => DugoutAt(ground, t), (spot, middle) => CanCutDoor(ground, spot, middle), out _, out var door))
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-no-dugout"), user, user);
            return;
        }

        // the door spot is a tile of the dug-out next door: the door goes through the partition between the two
        // the middle of the dug-out side this door opens: its tile next to the door spot
        var middle = door;
        foreach (var side in Cardinals)
        {
            if (DugoutAt(ground, door + side) != null)
            {
                middle = door + side;
                break;
            }
        }

        if (SeamBetween(ground, middle, door) is { } seam)
        {
            if (!NearTiles(userPos, [middle, door]))
            {
                _popup.PopupEntity(Loc.GetString("st14-trench-too-far"), user, user);
                return;
            }

            Del(seam);
            _popup.PopupEntity(Loc.GetString("st14-trench-door-cut"), user, user);
            return;
        }

        var blueprint = BlueprintAt(ground, door);
        if (blueprint == null)
        {
            if (!_zLevels.TryGetLayerGrid(ground, out var grid) || !IsGround(grid.Value, door))
            {
                _popup.PopupEntity(Loc.GetString("st14-trench-door-open"), user, user);
                return;
            }

            blueprint = TryLayOut(ground, [door], false, (float) digger.Comp.DigTime.TotalSeconds);
            if (blueprint is { } cut)
                Comp<ST14TrenchBlueprintComponent>(cut).Door = true;
        }

        if (blueprint is not { } segment)
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-bad-spot"), user, user);
            return;
        }

        if (NearTiles(userPos, Comp<ST14TrenchBlueprintComponent>(segment).Tiles))
            StartSwing(digger, user, segment);
        else
            _popup.PopupEntity(Loc.GetString("st14-trench-laid-out"), user, user);
    }

    /// <summary>
    /// A trench just dug next to a dug-out stays closed off from it: a partition along the edge they share. Only a door
    /// cut there opens it.
    /// </summary>
    private void CloseOffDugouts(EntityUid ground, List<Vector2i> tiles)
    {
        foreach (var tile in tiles)
        {
            foreach (var side in Cardinals)
            {
                var next = tile + side;
                if (_roofs.TryGetRoofEntity(ground, next, out var roof) && HasComp<ST14DugoutPartComponent>(roof))
                    PlaceSeam(ground, tile, side);
            }
        }
    }

    /// <summary>
    /// Whether a door can go on <paramref name="spot"/>, the door spot that opens <paramref name="middle"/>: untouched
    /// ground (it is dug out), a door being dug there, or a trench closed off by a partition (it is taken down). Not on a
    /// dug-out built on to this one - the two are one building - nor where it is open already.
    /// </summary>
    private bool CanCutDoor(EntityUid ground, Vector2i spot, Vector2i middle)
    {
        // a corner bunker has no middle on its cut sides: no door there
        if (DugoutAt(ground, spot) != null || DugoutAt(ground, middle) == null || !_zLevels.TryGetLayerGrid(ground, out var grid))
            return false;

        if (BlueprintAt(ground, spot) != null)
            return true;

        if (IsGround(grid.Value, spot))
            return !IsBlocked(grid.Value, spot);

        return SeamBetween(ground, middle, spot) != null;
    }
}
