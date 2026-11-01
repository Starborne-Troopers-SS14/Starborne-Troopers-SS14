using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Slime spit of the brain bug: every glob that lands sticks to the trooper, and enough of them wrap it in a
/// cocoon it cannot move in
/// </summary>
[RegisterComponent]
public sealed partial class ST14BrainSpitComponent : Component
{
    [DataField]
    public EntProtoId Projectile = "ST14BrainSlimeSpit";

    [DataField]
    public float Speed = 14f;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public EntProtoId Action = "ST14ActionBrainSpit";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// A glob of slime in flight. It flies through bugs and sticks to the first trooper it meets
/// </summary>
[RegisterComponent]
public sealed partial class ST14SlimeSpitComponent : Component;

/// <summary>
/// A trooper with slime on it. Enough globs and it is wrapped in a cocoon: no walking, shooting or hitting
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class ST14SlimedComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Globs;

    [DataField]
    public int GlobsNeeded = 3;

    /// <summary>
    /// Globs that do not become a cocoon dry off after this long
    /// </summary>
    [DataField]
    public TimeSpan GlobLifetime = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan CocoonTime = TimeSpan.FromSeconds(12);

    [DataField, AutoNetworkedField]
    public bool Cocooned;

    /// <summary>
    /// When the globs dry off, or the cocoon breaks
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;
}

/// <summary>
/// Scream of the brain bug: the troopers around see things that are not there and hear nothing but ringing
/// </summary>
[RegisterComponent]
public sealed partial class ST14BrainScreamComponent : Component
{
    [DataField]
    public float Range = 6f;

    [DataField]
    public TimeSpan Hallucinations = TimeSpan.FromSeconds(12);

    [DataField]
    public TimeSpan Deafness = TimeSpan.FromSeconds(10);

    [DataField]
    public EntProtoId HallucinationEffect = "StatusEffectSeeingRainbow";

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
/// A trooper deafened by the scream: no speech reaches it until the ringing stops
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ST14DeafenedComponent : Component
{
    [DataField, AutoPausedField]
    public TimeSpan End;
}
