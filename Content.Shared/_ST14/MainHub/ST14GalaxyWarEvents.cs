using System.Numerics;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// The commands and the sides of the war. A round decides one hex sector, see <see cref="ST14PlanetWar.ApplyResult"/>
/// </summary>
public static class ST14GalaxyWarRules
{
    /// <summary>
    /// Whitelist of the High Command: players who pick the sector to attack
    /// </summary>
    public static readonly ProtoId<JobPrototype> HighCommand = "ST14HighCommand";

    /// <summary>
    /// Whitelist of the command of the bugs: they plan the war of the hives on the same map
    /// </summary>
    public static readonly ProtoId<JobPrototype> HiveCommand = "ST14HiveCommand";

    /// <summary>
    /// Lobby theme of the bugs: a player whose high priority job is in it plays for the bugs
    /// </summary>
    public static readonly ProtoId<ST14LobbyThemePrototype> BugTheme = "ST14Arachnids";

    /// <summary>
    /// The sides that have a command of their own
    /// </summary>
    public static readonly ST14GalaxyFaction[] Sides = { ST14GalaxyFaction.Federation, ST14GalaxyFaction.Arachnid };

    public static ProtoId<JobPrototype> CommandOf(ST14GalaxyFaction side)
    {
        return side == ST14GalaxyFaction.Arachnid ? HiveCommand : HighCommand;
    }
}

/// <summary>
/// One hex sector as the server keeps it, with the dead counted in the rounds fought there
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14HexCellState(
    ST14GalaxyFaction Faction,
    float Liberation,
    bool Hive,
    bool Base,
    int BugsKilled,
    int TroopersKilled,
    bool Initialized);

/// <summary>
/// One battle of the war: which round, for which sector, who attacked and who won. No winner: the time ran out
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14WarLogEntry(int Round, int Cell, ST14GalaxyFaction Attacker, ST14GalaxyFaction? Winner);

/// <summary>
/// What the High Command can draw on the galaxy map. Arrows go from a point to a point, the rest are single marks
/// </summary>
public enum ST14WarMarkKind : byte
{
    Attack,
    Defend,
    Rally,
    Danger,
    Objective,
}

[Serializable, NetSerializable]
public readonly record struct ST14WarMark(ST14WarMarkKind Kind, Vector2 From, Vector2 To, bool Surface = false)
{
    // a mark on the surface of the war planet has longitude and latitude for its points, the others galaxy coordinates

    public bool IsArrow => Kind is ST14WarMarkKind.Attack or ST14WarMarkKind.Defend;
}

/// <summary>
/// The plan of the command of one side on the galaxy map: arrows and marks in galaxy coordinates and the standing order
/// shown at the top of the map. Each side only sees its own plan
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyOrdersEvent(ST14GalaxyFaction side, ST14WarMark[] marks, string order, bool canCommand, string[] members) : EntityEventArgs
{
    public readonly ST14GalaxyFaction Side = side;
    public const int MaxMarks = 40;
    public const int MaxOrderLength = 140;

    public readonly ST14WarMark[] Marks = marks;
    public readonly string Order = order;
    public readonly bool CanCommand = canCommand;

    /// <summary>
    /// Players of this command who are on the server now
    /// </summary>
    public readonly string[] Members = members;
}

[Serializable, NetSerializable]
public sealed class ST14GalaxyMarkAddEvent(ST14GalaxyFaction side, ST14WarMark mark) : EntityEventArgs
{
    public readonly ST14GalaxyFaction Side = side;
    public readonly ST14WarMark Mark = mark;
}

/// <summary>
/// Removes one mark by its index, or all of them with -1
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyMarkRemoveEvent(ST14GalaxyFaction side, int index) : EntityEventArgs
{
    public readonly ST14GalaxyFaction Side = side;
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class ST14GalaxyOrderTextEvent(ST14GalaxyFaction side, string order) : EntityEventArgs
{
    public readonly ST14GalaxyFaction Side = side;
    public readonly string Order = order;
}

/// <summary>
/// Client opened the galaxy map and wants the state of the war
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyWarRequestEvent : EntityEventArgs;

/// <summary>
/// State of the war on one planet: every hex sector, the sector the High Command picked to attack,
/// and whether the receiving player may change that target
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyWarStateEvent(string planet, ST14HexCellState[] cells, int target, int staging, ST14GalaxyFaction turn, bool canCommand, ST14WarLogEntry[] log, bool targetPicked, bool battle, float battleSecondsLeft) : EntityEventArgs
{
    public readonly string Planet = planet;
    public readonly ST14HexCellState[] Cells = cells;

    /// <summary>
    /// Index of the target sector, -1 when none is picked
    /// </summary>
    public readonly int Target = target;

    /// <summary>
    /// Neighbour of the target the attacker gathers in, -1 when it lands in the target itself
    /// </summary>
    public readonly int Staging = staging;

    /// <summary>
    /// The side whose command picks the next battle
    /// </summary>
    public readonly ST14GalaxyFaction Turn = turn;

    /// <summary>
    /// The player belongs to the command whose turn it is
    /// </summary>
    public readonly bool CanCommand = canCommand;

    /// <summary>
    /// The battles fought on the planet, the oldest first
    /// </summary>
    public readonly ST14WarLogEntry[] Log = log;

    /// <summary>
    /// The command whose turn it is picked the target itself, otherwise it is only the one the round would fall back on
    /// </summary>
    public readonly bool TargetPicked = targetPicked;

    /// <summary>
    /// A round is being fought over the target: the target and the staging sector are locked until it ends
    /// </summary>
    public readonly bool Battle = battle;

    public readonly float BattleSecondsLeft = battleSecondsLeft;
}

/// <summary>
/// The command whose turn it is picks the sector the attacker gathers in, next to the target
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyWarStagingEvent(string planet, int cell) : EntityEventArgs
{
    public readonly string Planet = planet;
    public readonly int Cell = cell;
}

/// <summary>
/// The command whose turn it is picks the sector of the next battle. The server checks the whitelist and the options
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyWarOrderEvent(string planet, int cell) : EntityEventArgs
{
    public readonly string Planet = planet;
    public readonly int Cell = cell;
}
