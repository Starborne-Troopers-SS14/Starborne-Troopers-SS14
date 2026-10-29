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

[Serializable, NetSerializable]
public readonly record struct ST14HexCellState(ST14GalaxyFaction Faction, float Liberation, bool Hive, bool Base);

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
