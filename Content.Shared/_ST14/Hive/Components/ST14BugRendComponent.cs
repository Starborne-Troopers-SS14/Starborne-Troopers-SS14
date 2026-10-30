using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// The bug spins and slashes everything around it that is not of the hive
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugRendComponent : Component
{
    [DataField]
    public float Range = 1.6f;

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// How long the troopers caught in the spin are staggered
    /// </summary>
    [DataField]
    public TimeSpan Stun = TimeSpan.FromSeconds(0.6);

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public EntProtoId Action = "ST14ActionBugRend";

    [DataField]
    public EntityUid? ActionEntity;
}
