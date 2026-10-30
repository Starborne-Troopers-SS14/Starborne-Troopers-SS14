using System.Numerics;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// How a round moves the bar of the hex sector it was fought in. A sector is taken at 100%,
/// Federation ground is lost again when its bar falls to 0
/// </summary>
public static class ST14GalaxyWarRules
{
    public const float WinGain = 20;

    /// <summary>
    /// A win with the objectives done, like a destroyed hive
    /// </summary>
    public const float ObjectiveGain = 30;

    public const float LossPenalty = 20;

    /// <summary>
    /// Whitelist of the High Command: players who pick the sector to attack
    /// </summary>
    public static readonly ProtoId<JobPrototype> HighCommand = "ST14HighCommand";
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
    int TroopersKilled);

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
public readonly record struct ST14WarMark(ST14WarMarkKind Kind, Vector2 From, Vector2 To)
{
    public bool IsArrow => Kind is ST14WarMarkKind.Attack or ST14WarMarkKind.Defend;
}

/// <summary>
/// The plan of the High Command on the galaxy map: arrows and marks in galaxy coordinates and the standing order
/// shown to everyone at the top of the map
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyOrdersEvent(ST14WarMark[] marks, string order, bool canCommand) : EntityEventArgs
{
    public const int MaxMarks = 40;
    public const int MaxOrderLength = 140;

    public readonly ST14WarMark[] Marks = marks;
    public readonly string Order = order;
    public readonly bool CanCommand = canCommand;
}

[Serializable, NetSerializable]
public sealed class ST14GalaxyMarkAddEvent(ST14WarMark mark) : EntityEventArgs
{
    public readonly ST14WarMark Mark = mark;
}

/// <summary>
/// Removes one mark by its index, or all of them with -1
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyMarkRemoveEvent(int index) : EntityEventArgs
{
    public readonly int Index = index;
}

[Serializable, NetSerializable]
public sealed class ST14GalaxyOrderTextEvent(string order) : EntityEventArgs
{
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
public sealed class ST14GalaxyWarStateEvent(string planet, ST14HexCellState[] cells, int target, bool canCommand) : EntityEventArgs
{
    public readonly string Planet = planet;
    public readonly ST14HexCellState[] Cells = cells;

    /// <summary>
    /// Index of the target sector, -1 when none is picked
    /// </summary>
    public readonly int Target = target;

    public readonly bool CanCommand = canCommand;
}

/// <summary>
/// A member of the High Command picks the sector of the next attack. The server checks the whitelist and the front
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14GalaxyWarOrderEvent(string planet, int cell) : EntityEventArgs
{
    public readonly string Planet = planet;
    public readonly int Cell = cell;
}
