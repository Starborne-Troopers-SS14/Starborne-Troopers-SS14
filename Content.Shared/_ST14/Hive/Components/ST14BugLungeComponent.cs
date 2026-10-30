using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// The bug leaps at a point. The first trooper it crashes into is knocked down and wounded
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugLungeComponent : Component
{
    /// <summary>
    /// Longest leap, in tiles. A closer target is leapt at exactly
    /// </summary>
    [DataField]
    public float Distance = 4f;

    [DataField]
    public float Speed = 12f;

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(1.5);

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public EntProtoId Action = "ST14ActionBugLunge";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// A bug in the middle of its leap: the next trooper it hits takes the blow
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugLungingComponent : Component;
