using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Execution: the bug leaps at a trooper. A badly wounded one is finished on the spot and the bug heals. The bug will not leap at a trooper that is not wounded badly enough
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugExecuteComponent : Component
{
    /// <summary>
    /// A trooper with less health left than this part of it can be executed. Critical troopers always can
    /// </summary>
    [DataField]
    public float HealthThreshold = 0.3f;

    [DataField]
    public FixedPoint2 Heal = 50;

    /// <summary>
    /// Longest leap, in tiles
    /// </summary>
    [DataField]
    public float MaxLeap = 10f;

    /// <summary>
    /// How far from the victim the bug may land and still strike, in tiles
    /// </summary>
    [DataField]
    public float StrikeRange = 1.6f;

    [DataField]
    public float Speed = 14f;

    [DataField]
    public SoundSpecifier? LeapSound;

    [DataField]
    public SoundSpecifier? KillSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBugExecute";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// A bug in the middle of an execution leap at its victim
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugExecutingComponent : Component
{
    [DataField]
    public EntityUid Target;
}
