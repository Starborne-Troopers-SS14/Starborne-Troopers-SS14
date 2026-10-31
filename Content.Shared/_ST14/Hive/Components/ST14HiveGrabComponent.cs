using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Grip of the hive: the bug clamps a trooper in its claws and drags it off to the brain bug.
/// The trooper cannot shoot or walk until it struggles free or its friends shoot the bug hard enough
/// </summary>
[RegisterComponent]
public sealed partial class ST14HiveGrabComponent : Component
{
    /// <summary>
    /// Damage the bug takes before it lets go
    /// </summary>
    [DataField]
    public FixedPoint2 BreakDamage = 40;

    /// <summary>
    /// How close the brain bug has to be for the delivery to start the sucking, in tiles
    /// </summary>
    [DataField]
    public float DeliveryRange = 1.8f;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public SoundSpecifier? ReleaseSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBugGrab";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// The trooper in the claws right now
    /// </summary>
    [DataField]
    public EntityUid? Victim;

    /// <summary>
    /// Damage taken since the grab started
    /// </summary>
    [DataField]
    public FixedPoint2 DamageTaken;
}

/// <summary>
/// A trooper held in the claws of a bug: no shooting, no walking, only struggling
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14HiveGrabbedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Grabber;

    /// <summary>
    /// Struggle built up by mashing the movement keys. The trooper is free once it reaches <see cref="StruggleNeeded"/>
    /// </summary>
    [DataField]
    public float Struggle;

    [DataField]
    public float StruggleNeeded = 14f;

    /// <summary>
    /// Struggle lost every second, so only quick and steady mashing breaks the grip
    /// </summary>
    [DataField]
    public float StruggleDecay = 2.5f;
}
