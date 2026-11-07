using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Sectors;

/// <summary>
/// The shape every sector map shares: a hexagon with its points up in a square of tiles, with its middle in the middle
/// of the square. Edges count counter-clockwise from the east
/// </summary>
public static class ST14SectorGeometry
{
    public const int Size = 200;

    /// <summary>
    /// From the centre to the middle of an edge
    /// </summary>
    public const float Apothem = 83f;

    /// <summary>
    /// From the centre to a corner
    /// </summary>
    public static readonly float Radius = Apothem / MathF.Cos(MathF.PI / 6);

    public static readonly Vector2 Center = new(Size / 2f, Size / 2f);

    public static Vector2 EdgeNormal(int edge)
    {
        var angle = MathF.PI / 3 * edge;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    public static bool Contains(Vector2 point)
    {
        var offset = point - Center;
        for (var edge = 0; edge < 6; edge++)
        {
            if (Vector2.Dot(offset, EdgeNormal(edge)) > Apothem)
                return false;
        }

        return true;
    }
}

/// <summary>
/// A mark of a command on the map of one sector, in tiles of that sector. Arrows go from a point to a point
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14BattleMark(ST14WarMarkKind Kind, int Cell, Vector2 From, Vector2 To)
{
    public bool IsArrow => Kind is ST14WarMarkKind.Attack or ST14WarMarkKind.Defend;
}

/// <summary>
/// A sector on the battle map. Edges name the neighbour behind each passage and are known for the loaded sectors only
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14BattleSector(int Cell, string Code, ST14GalaxyFaction Faction, bool Loaded, int[] Edges);

/// <summary>
/// The player opened the battle map. A member of both commands can ask for the plan of either
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BattleMapRequestEvent(ST14GalaxyFaction? side) : EntityEventArgs
{
    public readonly ST14GalaxyFaction? Side = side;
}

/// <summary>
/// The battle map for one side: only the plan of that side ever goes out
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BattleMapStateEvent(
    bool active,
    int round,
    string planet,
    ST14GalaxyFaction side,
    ST14GalaxyFaction[] commands,
    int here,
    int target,
    int staging,
    ST14BattleSector[] sectors,
    ST14BattleMark[] marks) : EntityEventArgs
{
    public const int MaxMarks = 60;

    /// <summary>
    /// A sector round is being fought
    /// </summary>
    public readonly bool Active = active;

    public readonly int Round = round;

    /// <summary>
    /// The war planet of the round, empty without a sector round
    /// </summary>
    public readonly string Planet = planet;

    public readonly ST14GalaxyFaction Side = side;

    /// <summary>
    /// The commands this player belongs to, they may draw for these
    /// </summary>
    public readonly ST14GalaxyFaction[] Commands = commands;

    /// <summary>
    /// The sector the player is in, -1 when they are not in one
    /// </summary>
    public readonly int Here = here;

    public readonly int Target = target;
    public readonly int Staging = staging;
    public readonly ST14BattleSector[] Sectors = sectors;
    public readonly ST14BattleMark[] Marks = marks;
}

[Serializable, NetSerializable]
public sealed class ST14BattleMapTerrainRequestEvent(int cell) : EntityEventArgs
{
    public readonly int Cell = cell;
}

/// <summary>
/// The ground of a loaded sector, one value for every block of tiles, row by row from the bottom left
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BattleMapTerrainEvent(int round, int cell, byte[] blocks) : EntityEventArgs
{
    /// <summary>
    /// Tiles along the side of one block
    /// </summary>
    public const int BlockTiles = 2;

    public const int Blocks = ST14SectorGeometry.Size / BlockTiles;

    public const byte Empty = 0;
    public const byte Ground = 1;
    public const byte Obstacle = 2;

    public readonly int Round = round;
    public readonly int Cell = cell;
    public readonly byte[] Data = blocks;
}

[Serializable, NetSerializable]
public sealed class ST14BattleMarkAddEvent(ST14GalaxyFaction side, ST14BattleMark mark) : EntityEventArgs
{
    public readonly ST14GalaxyFaction Side = side;
    public readonly ST14BattleMark Mark = mark;
}

/// <summary>
/// Removes one mark by its index, or all of them with -1
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BattleMarkRemoveEvent(ST14GalaxyFaction side, int index) : EntityEventArgs
{
    public readonly ST14GalaxyFaction Side = side;
    public readonly int Index = index;
}

/// <summary>
/// Puts a thing on the battle map as its own icon: a drill now, walls, turrets and factories later. Without a side
/// everybody sees it, with one only that side does
/// </summary>
[RegisterComponent]
public sealed partial class ST14MapIconComponent : Component
{
    [DataField]
    public ST14GalaxyFaction? Side;
}

/// <summary>
/// The window of the battle map closed: the server stops sending what moves
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BattleMapClosedEvent : EntityEventArgs;

/// <summary>
/// A thing with an icon, where it stands in the tiles of its sector
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14MapIconInfo(int Cell, Vector2 Position, string Prototype);

public enum ST14SquadMemberStatus : byte
{
    Alive,
    Critical,
    Dead,

    /// <summary>
    /// Not in the round: no body, or not on the server
    /// </summary>
    Away,
}

[Serializable, NetSerializable]
public readonly record struct ST14SquadMember(string Name, ST14SquadMemberStatus Status, bool Leader);

/// <summary>
/// A squad of one side. The number names it
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14SquadInfo(int Id, int Number, ST14SquadMember[] Members);

/// <summary>
/// Somebody of the squad asked this player to join it
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14SquadInvite(int Squad, int Number, string From);

/// <summary>
/// The squads of the side of the player, sent whenever one of them changes
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14SquadStateEvent(bool active, int round, ST14SquadInfo[] squads, int squad, ST14SquadInvite? invite) : EntityEventArgs
{
    public const int MaxSquadSize = 8;

    /// <summary>
    /// A sector round is being fought, squads only exist in one
    /// </summary>
    public readonly bool Active = active;

    public readonly int Round = round;
    public readonly ST14SquadInfo[] Squads = squads;

    /// <summary>
    /// Id of the squad of the player, -1 without one
    /// </summary>
    public readonly int Squad = squad;

    public readonly ST14SquadInvite? Invite = invite;
}

/// <summary>
/// A member of the squad of the player on the map, the player among them
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14SquadMate(string Name, int Cell, Vector2 Position, bool Leader, bool Me);

/// <summary>
/// What moves on the map, sent every second while it is open: the icons and where the squad is
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BattleMapLiveEvent(int round, ST14MapIconInfo[] icons, ST14SquadMate[] mates, ST14SubsectorPoint[] points) : EntityEventArgs
{
    public readonly int Round = round;
    public readonly ST14MapIconInfo[] Icons = icons;
    public readonly ST14SquadMate[] Mates = mates;

    /// <summary>
    /// The resource points of the loaded sectors, each the middle of a subsector
    /// </summary>
    public readonly ST14SubsectorPoint[] Points = points;
}

[Serializable, NetSerializable]
public sealed class ST14SquadRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class ST14SquadCreateEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class ST14SquadJoinEvent(int id) : EntityEventArgs
{
    public readonly int Id = id;
}

[Serializable, NetSerializable]
public sealed class ST14SquadLeaveEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class ST14SquadInviteAnswerEvent(bool accept) : EntityEventArgs
{
    public readonly bool Accept = accept;
}
