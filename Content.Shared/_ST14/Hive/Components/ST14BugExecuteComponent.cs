using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Execution: the bug leaps at a trooper. A badly wounded one is finished on the spot, the bug heals
/// and may leap at the next one right away. A healthy one only takes a normal blow
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
    /// The blow a trooper takes when it is too healthy to be executed
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// How far from the victim the bug may land and still strike, in tiles
    /// </summary>
    [DataField]
    public float StrikeRange = 1.6f;

    [DataField]
    public float Speed = 14f;

    /// <summary>
    /// Cooldown left after a successful execution, so the next one is a moment away
    /// </summary>
    [DataField]
    public TimeSpan ResetCooldown = TimeSpan.FromSeconds(1);

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
