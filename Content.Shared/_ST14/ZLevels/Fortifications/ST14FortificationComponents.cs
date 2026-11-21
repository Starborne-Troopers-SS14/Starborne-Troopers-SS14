/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Shared.DoAfter;
using Content.Shared.Maps;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.ZLevels.Fortifications;

/// <summary>
/// What the entrenching tool does when used on the ground, switched by using it in hand
/// </summary>
[Serializable, NetSerializable]
public enum ST14DigMode : byte
{
    /// <summary>
    /// Lays out a straight trench segment and digs it
    /// </summary>
    Trench,

    /// <summary>
    /// Lays out a square dug-out and digs it: it is roofed as soon as it is dug. Trenches have no roof
    /// </summary>
    Dugout,

    /// <summary>
    /// Cuts a door into a dug-out: the middle of the side nearest the cursor, one tile dug out through its wall
    /// </summary>
    Door,

    /// <summary>
    /// Cuts steps out of a trench at the tile used on, or fills them in
    /// </summary>
    Steps,

    /// <summary>
    /// Digs small cable trenches (<c>ST14TrenchTool</c>)
    /// </summary>
    Cable,

    /// <summary>
    /// Fills a tile of a trench back in, the ground over it as it was (Foxhole: "bury")
    /// </summary>
    Fill,

    /// <summary>
    /// Lays out a corner bunker and digs it: the half of a 3x3 square on one side of its diagonal, as a staircase of
    /// six tiles, roofed like a dug-out. It joins two bunkers at a corner. The direction picks the corner of the right
    /// angle.
    /// </summary>
    Corner,
}

/// <summary>
/// A tool that digs trenches the Foxhole way: used on the ground of the surface it lays out a segment (a frame of
/// stakes and string) and digs it out bit by bit while the soldier keeps at it. A dug segment opens into the level
/// below, where it is the floor of a trench walled with earth. A square dug-out is roofed as soon as it is dug; a
/// trench stays open. The same tool cuts steps and digs cable trenches; using it in hand switches between them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14TrenchDiggerComponent : Component
{
    /// <summary>
    /// How long one tile of a segment takes to dig
    /// </summary>
    [DataField]
    public TimeSpan DigTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// One swing of the spade: progress is shown and the work can be broken off after each
    /// </summary>
    [DataField]
    public TimeSpan SwingTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long cutting or filling in steps takes (Foxhole: steps are an upgrade of the trench)
    /// </summary>
    [DataField]
    public TimeSpan UpgradeTime = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Tiles in a straight trench segment along the way it is turned, 1 to <see cref="MaxTrenchLength"/>, picked in
    /// the build window. The time to dig it grows with its tiles.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int TrenchLength = 3;

    /// <summary>
    /// Tiles across a straight trench segment, 1 to <see cref="MaxTrenchWidth"/>
    /// </summary>
    [DataField, AutoNetworkedField]
    public int TrenchWidth = 1;

    public const int MaxTrenchLength = 5;
    public const int MaxTrenchWidth = 2;

    /// <summary>
    /// The side of a square dug-out in tiles
    /// </summary>
    [DataField]
    public int DugoutSize = 3;

    [DataField, AutoNetworkedField]
    public ST14DigMode Mode = ST14DigMode.Trench;

    /// <summary>
    /// How the next segment is turned (R in the build window). For steps, the way they climb out
    /// </summary>
    [DataField, AutoNetworkedField]
    public Direction Direction = Direction.South;

    /// <summary>
    /// Digs cable trenches instead of trenches into the level below
    /// </summary>
    public bool CableMode => Mode == ST14DigMode.Cable;
}

/// <summary>
/// A trench segment laid out on the ground and being dug. When the work is done every tile of it opens into the level
/// below at once. Its stakes are separate entities (<see cref="Markers"/>), one per tile.
/// </summary>
[RegisterComponent]
public sealed partial class ST14TrenchBlueprintComponent : Component
{
    /// <summary>
    /// World tiles of the level it stands on
    /// </summary>
    [DataField]
    public List<Vector2i> Tiles = new();

    /// <summary>
    /// Seconds of digging done
    /// </summary>
    [DataField]
    public float Progress;

    /// <summary>
    /// Seconds of digging it takes
    /// </summary>
    [DataField]
    public float Total = 1f;

    /// <summary>
    /// A dug-out: roofed as soon as it is dug
    /// </summary>
    [DataField]
    public bool Roofed;

    /// <summary>
    /// A door cut into a dug-out: dug, it opens straight into it, with no partition between
    /// </summary>
    [DataField]
    public bool Door;

    [DataField]
    public List<EntityUid> Markers = new();
}

/// <summary>
/// Something to climb up over to the level above, with Alt+click: the side of a trench (out onto the ground; steps
/// are the quick way) or the wall of a bunker (up onto its roof).
/// </summary>
[RegisterComponent]
public sealed partial class ST14ClimbUpComponent : Component
{
    [DataField]
    public TimeSpan ClimbTime = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// The verb, a locale id
    /// </summary>
    [DataField]
    public LocId Verb = "st14-climb-out-of-trench";
}

[Serializable, NetSerializable]
public sealed partial class ST14DigBlueprintDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ST14ClimbOutOfTrenchDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Going along a ramp or up/down a ladder of a bunker, to the other end of it
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ST14BunkerPassageDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Filling a trench tile back in with the spade
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ST14FillTrenchDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetCoordinates Location;

    private ST14FillTrenchDoAfterEvent()
    {
    }

    public ST14FillTrenchDoAfterEvent(NetCoordinates location)
    {
        Location = location;
    }

    public override DoAfterEvent Clone() => this;
}

/// <summary>
/// Getting down into a trench carefully, or up out of it onto the ground, at the tile Alt+clicked
/// </summary>
[Serializable, NetSerializable]
public sealed partial class ST14TrenchClimbDoAfterEvent : DoAfterEvent
{
    /// <summary>
    /// The middle of the tile to end up on
    /// </summary>
    [DataField]
    public NetCoordinates Location;

    [DataField]
    public bool Down;

    private ST14TrenchClimbDoAfterEvent()
    {
    }

    public ST14TrenchClimbDoAfterEvent(NetCoordinates location, bool down)
    {
        Location = location;
        Down = down;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed partial class ST14TrenchStepsDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetCoordinates Location;

    /// <summary>
    /// Which way the steps climb out; <see cref="Direction.Invalid"/> picks the end of the trench
    /// </summary>
    [DataField]
    public Direction Climb = Direction.Invalid;

    private ST14TrenchStepsDoAfterEvent()
    {
    }

    public ST14TrenchStepsDoAfterEvent(NetCoordinates location, Direction climb = Direction.Invalid)
    {
        Location = location;
        Climb = climb;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}

/// <summary>
/// The client picked a job for the entrenching tool in its build window, or turned it with R
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14SetDigModeEvent : EntityEventArgs
{
    public NetEntity Tool;
    public ST14DigMode Mode;
    public Direction Direction = Direction.South;

    /// <summary>
    /// The size of a trench segment, see <see cref="ST14TrenchDiggerComponent.TrenchLength"/>
    /// </summary>
    public int Length = 3;
    public int Width = 1;
}

/// <summary>
/// Raised on the client when the entrenching tool is used in hand: the build window opens
/// </summary>
public readonly record struct ST14OpenTrenchMenuEvent(EntityUid Tool);

/// <summary>
/// The tiles a segment covers, the same on the client ghost and the server
/// </summary>
public static class ST14TrenchShapes
{
    /// <summary>
    /// The segment laid out at the cursor, <paramref name="world"/> being where it points. A trench runs through the
    /// tile under it along <paramref name="direction"/>, centred on it; a dug-out is a square centred on the cursor
    /// (round the corner of tiles nearest it for an even size)
    /// </summary>
    public static List<Vector2i> Shape(ST14DigMode mode, System.Numerics.Vector2 world, Direction direction, int trenchLength = 3,
        int dugoutSize = 3, int trenchWidth = 1)
    {
        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (mode is not (ST14DigMode.Dugout or ST14DigMode.Corner))
            return Shape(mode, tile, direction, trenchLength, dugoutSize, trenchWidth);

        return BunkerShape(mode, BunkerStart(world, dugoutSize), direction, dugoutSize);
    }

    /// <summary>
    /// The corner of the square of a bunker centred on the cursor (round the corner of tiles nearest it)
    /// </summary>
    public static Vector2i BunkerStart(System.Numerics.Vector2 world, int size)
    {
        return new Vector2i((int) MathF.Round(world.X - size / 2f), (int) MathF.Round(world.Y - size / 2f));
    }

    /// <summary>
    /// The corner of the square of a new bunker at the cursor, flush against the bunkers already there: a bunker is
    /// built on to another one only side to side and edge to edge, so the cursor near one snaps to the nearest free
    /// place right next to it (east, west, north or south of it). Away from any bunker it goes where the cursor is.
    /// </summary>
    /// <param name="bunkerAt">the bunker a tile belongs to: its middle and size, or null</param>
    public static Vector2i SnappedBunkerStart(System.Numerics.Vector2 world, int size,
        Func<Vector2i, (Vector2i Center, int Size)?> bunkerAt)
    {
        var raw = BunkerStart(world, size);

        // the bunkers round the cursor
        var near = new List<(Vector2i Center, int Size)>();
        for (var x = -size; x < size * 2; x++)
        {
            for (var y = -size; y < size * 2; y++)
            {
                if (bunkerAt(raw + new Vector2i(x, y)) is { } found && !near.Contains(found))
                    near.Add(found);
            }
        }

        if (near.Count == 0)
            return raw;

        Vector2i? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (center, otherSize) in near)
        {
            var reach = (size + otherSize) / 2;
            foreach (var side in new[] { new Vector2i(reach, 0), new Vector2i(-reach, 0), new Vector2i(0, reach), new Vector2i(0, -reach) })
            {
                var middle = center + side;
                var start = middle - new Vector2i(size / 2, size / 2);
                if (!Free(start, size, bunkerAt))
                    continue;

                var distance = (middle + new System.Numerics.Vector2(0.5f) - world).Length();
                if (distance < bestDistance)
                {
                    best = start;
                    bestDistance = distance;
                }
            }
        }

        // the cursor far from every free place next to them: where it is (the server says if that will not do)
        return best is { } snapped && bestDistance <= size ? snapped : raw;
    }

    private static bool Free(Vector2i start, int size, Func<Vector2i, (Vector2i Center, int Size)?> bunkerAt)
    {
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                if (bunkerAt(start + new Vector2i(x, y)) != null)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The tiles of a bunker (a square) or a corner bunker (the staircase half of it) with its corner at
    /// <paramref name="start"/>
    /// </summary>
    public static List<Vector2i> BunkerShape(ST14DigMode mode, Vector2i start, Direction direction, int dugoutSize)
    {
        var last = dugoutSize - 1;
        var tiles = new List<Vector2i>();
        for (var x = 0; x < dugoutSize; x++)
        {
            for (var y = 0; y < dugoutSize; y++)
            {
                // a corner bunker: the tiles on the side of the diagonal where its right angle is, the diagonal with
                // them. South puts the right angle south-west, and R turns it on clockwise: west, north, east
                if (mode == ST14DigMode.Corner)
                {
                    var (a, b) = direction switch
                    {
                        Direction.West => (x, last - y),
                        Direction.North => (last - x, last - y),
                        Direction.East => (last - x, y),
                        _ => (x, y),
                    };

                    if (a + b > last)
                        continue;
                }

                tiles.Add(start + new Vector2i(x, y));
            }
        }

        return tiles;
    }

    /// <summary>
    /// A trench runs through <paramref name="tile"/> along <paramref name="direction"/>, centred on it; a dug-out is a
    /// square with a corner on it, turned by the direction
    /// </summary>
    public static List<Vector2i> Shape(ST14DigMode mode, Vector2i tile, Direction direction, int trenchLength = 3, int dugoutSize = 3,
        int trenchWidth = 1)
    {
        var along = direction.ToIntVec();
        if (along == Vector2i.Zero)
            along = new Vector2i(0, -1);

        var tiles = new List<Vector2i>();
        switch (mode)
        {
            case ST14DigMode.Trench:
                // length along the way it is turned, centred on the tile; a second row, if any, clockwise of it
                var start = -(trenchLength - 1) / 2;
                var side = new Vector2i(along.Y, -along.X);
                for (var w = 0; w < trenchWidth; w++)
                {
                    for (var i = 0; i < trenchLength; i++)
                    {
                        tiles.Add(tile + along * (start + i) + side * w);
                    }
                }

                break;
            case ST14DigMode.Dugout:
                // clockwise of along
                var across = new Vector2i(along.Y, -along.X);
                for (var a = 0; a < dugoutSize; a++)
                {
                    for (var b = 0; b < dugoutSize; b++)
                    {
                        tiles.Add(tile + along * a + across * b);
                    }
                }

                break;
            default:
                tiles.Add(tile);
                break;
        }

        return tiles;
    }
}

/// <summary>
/// A frame that roofs a bunker or a dug-out and then disappears: it puts <see cref="Roof"/> on the level above itself
/// wherever that level is open (a dug-out under the ground, a bunker under the open air). The roof covers it from the
/// sky, the light and the weather, and is hidden from whoever stands under it.
/// </summary>
[RegisterComponent]
public sealed partial class ST14RoofPlacerComponent : Component
{
    /// <summary>
    /// The roof to lay; by default a dug-out roof on the surface and a bunker roof higher up
    /// </summary>
    [DataField]
    public EntProtoId? Roof;

    /// <summary>
    /// 0 covers the tile of the frame, 1 a 3x3 square around it, and so on
    /// </summary>
    [DataField]
    public int Radius = 1;
}

/// <summary>
/// The roof of a bunker or a dug-out on the level above it. Like a roof tile it keeps the sky, the light and the
/// weather off the tile under it, but it is an entity: the client hides every roof joined to the one over the player,
/// so whoever goes into a bunker sees inside it and the rest of the line keeps its roofs.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14BunkerRoofComponent : Component;

/// <summary>
/// Which sides of a tile are the edge of a shape (a trench, a layout): the wooden rim around a trench and the string of
/// a layout are drawn only there, so a shape of many tiles has one outline. The diagonals are its outer corners.
/// </summary>
[Serializable, NetSerializable]
public enum ST14OutlineVisuals : byte
{
    North,
    South,
    East,
    West,
    NorthEast,
    NorthWest,
    SouthEast,
    SouthWest,

    /// <summary>
    /// An inner corner: both sides next to it go on in the shape, the tile across the corner does not
    /// </summary>
    InnerNorthEast,
    InnerNorthWest,
    InnerSouthEast,
    InnerSouthWest,
}

/// <summary>
/// The wooden rim on the ground around one dug tile of a trench (<see cref="ST14OutlineVisuals"/>)
/// </summary>
[RegisterComponent]
public sealed partial class ST14TrenchRimComponent : Component;

/// <summary>
/// One tile of a dug-out, on its roof. A dug-out is closed all round when it is dug: the only ways in are the middle of
/// each side, where a trench may be dug up to it (a door), so the soldiers choose where its doors are, at most four.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14DugoutPartComponent : Component
{
    /// <summary>
    /// The middle of the dug-out, in world tiles of the surface
    /// </summary>
    [DataField, AutoNetworkedField]
    public Vector2i Center;

    /// <summary>
    /// Its side in tiles
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Size = 3;
}

/// <summary>
/// The looks of a dug-out roof besides its outline
/// </summary>
[Serializable, NetSerializable]
public enum ST14DugoutVisuals : byte
{
    /// <summary>
    /// The ladder leaning on the middle of its south side, to show it can be climbed
    /// </summary>
    Ladder,
}

/// <summary>
/// Where the doors of a dug-out can go: the tile just outside the middle of each side. The client ghost and the server
/// pick the same one, the spot nearest the cursor.
/// </summary>
public static class ST14DugoutDoors
{
    /// <summary>
    /// The tile of the dug-out a door spot opens: the middle of that side, one step back towards the middle
    /// </summary>
    public static Vector2i Middle(Vector2i center, Vector2i spot)
    {
        return spot - new Vector2i(Math.Sign(spot.X - center.X), Math.Sign(spot.Y - center.Y));
    }

    /// <summary>
    /// The four door spots of a dug-out
    /// </summary>
    public static Vector2i[] Spots(Vector2i center, int size)
    {
        var reach = size / 2 + 1;
        return
        [
            center + new Vector2i(0, reach),
            center + new Vector2i(0, -reach),
            center + new Vector2i(reach, 0),
            center + new Vector2i(-reach, 0),
        ];
    }

    /// <summary>
    /// The dug-out nearest the cursor (one of its tiles within two tiles of it) and its door spots where a door can go,
    /// the one nearest the cursor first. None can go: false, and nothing is shown
    /// </summary>
    /// <param name="dugoutAt">The dug-out a world tile belongs to, if any</param>
    /// <param name="possible">Whether a door can go on a spot (the spot, the middle of the dug-out side it opens)</param>
    public static bool TryFind(System.Numerics.Vector2 cursor, Func<Vector2i, (Vector2i Center, int Size)?> dugoutAt,
        Func<Vector2i, Vector2i, bool> possible, out Vector2i[] spots, out Vector2i door)
    {
        spots = [];
        door = default;
        var tile = new Vector2i((int) MathF.Floor(cursor.X), (int) MathF.Floor(cursor.Y));
        (Vector2i Center, int Size)? best = null;
        var bestDistance = float.MaxValue;
        for (var dx = -2; dx <= 2; dx++)
        {
            for (var dy = -2; dy <= 2; dy++)
            {
                if (dugoutAt(tile + new Vector2i(dx, dy)) is not { } dugout)
                    continue;

                var distance = (cursor - (dugout.Center + new System.Numerics.Vector2(0.5f))).LengthSquared();
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = dugout;
                }
            }
        }

        if (best is not { } found)
            return false;

        var all = Spots(found.Center, found.Size);
        var count = 0;
        foreach (var spot in all)
        {
            if (possible(spot, Middle(found.Center, spot)))
                count++;
        }

        spots = new Vector2i[count];
        count = 0;
        foreach (var spot in all)
        {
            if (possible(spot, Middle(found.Center, spot)))
                spots[count++] = spot;
        }

        if (spots.Length == 0)
            return false;

        var nearest = float.MaxValue;
        foreach (var spot in spots)
        {
            var distance = (cursor - (spot + new System.Numerics.Vector2(0.5f))).LengthSquared();
            if (distance < nearest)
            {
                nearest = distance;
                door = spot;
            }
        }

        return true;
    }
}
