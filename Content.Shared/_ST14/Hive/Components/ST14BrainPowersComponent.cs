using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Brain stupor: the brain bug reaches into the mind of one trooper. Its screen fills with the worst visions
/// and it cannot move until they let go
/// </summary>
[RegisterComponent]
public sealed partial class ST14BrainStuporComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Farthest trooper the mind reaches, in tiles
    /// </summary>
    [DataField]
    public float Range = 10f;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// What the trooper hears in its head, played only to it
    /// </summary>
    [DataField]
    public SoundSpecifier? VictimSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBrainStupor";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// Scream of the brain bug: the troopers around see things that are not there and hear nothing but ringing
/// </summary>
[RegisterComponent]
public sealed partial class ST14BrainScreamComponent : Component
{
    [DataField]
    public float Range = 7f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How hard the visions hit, 1 is the stupor
    /// </summary>
    [DataField]
    public float Intensity = 0.55f;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Ringing in the ears, heard only by the deafened
    /// </summary>
    [DataField]
    public SoundSpecifier? RingingSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBrainScream";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// A trooper whose head the brain bug got into: visions on its screen, and with the stupor no moving at all
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class ST14HallucinatingComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;

    /// <summary>
    /// How hard the visions hit, from 0 to 1
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Intensity;

    /// <summary>
    /// Frozen in place: no walking, shooting or hitting
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Paralyzed;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ParalyzedEnd;
}

/// <summary>
/// A trooper deafened by the scream: no speech reaches it until the ringing stops
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ST14DeafenedComponent : Component
{
    [DataField, AutoPausedField]
    public TimeSpan End;
}
