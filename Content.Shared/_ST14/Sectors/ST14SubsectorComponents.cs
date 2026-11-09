using System.Numerics;
using Content.Shared._ST14.MainHub;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Sectors;

/// <summary>
/// A building that holds the resource point it stands on for its side: a drill of the Federation, a nest of the hive.
/// A sector is split into subsectors around its resource points, and the side holding all of them at the end of a round
/// takes the sector
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14ResourcePointHolderComponent : Component
{
    [DataField(required: true)]
    public ST14GalaxyFaction Side;
}

/// <summary>
/// A bug that can grow a nest over a resource point it stands next to
/// </summary>
[RegisterComponent]
public sealed partial class ST14NestPlanterComponent : Component
{
    [DataField]
    public EntProtoId Action = "ST14ActionPlantNest";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public EntProtoId Nest = "ST14HiveNest";

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How far from the resource point the bug may stand
    /// </summary>
    [DataField]
    public float Range = 1.5f;
}

public sealed partial class ST14PlantNestActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class ST14PlantNestDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// A resource point of a sector of the battle, the middle of its subsector: where it lies in the tiles of the sector
/// and the side holding it, null when nobody or both sides stand on it
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14SubsectorPoint(int Cell, Vector2 Position, ST14GalaxyFaction? Holder);
