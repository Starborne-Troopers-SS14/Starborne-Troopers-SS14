using Content.Shared.Chat.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// A bug of the hive. Holds its caste and its own evolution points, earned by being useful to the hive
/// </summary>
[RegisterComponent]
public sealed partial class ST14HiveMemberComponent : Component
{
    /// <summary>
    /// The caste of this bug.
    /// </summary>
    [DataField]
    public ProtoId<ST14HiveCastePrototype>? Caste;

    /// <summary>
    /// Personal evolution points. Fractions pile up from small actions like single bites
    /// </summary>
    [DataField]
    public float EvolutionPoints;

    /// <summary>
    /// Points for every unit of damage this bug deals to something outside the hive: troopers, drills, walls
    /// </summary>
    [DataField]
    public float PointsPerDamage = 0.2f;

    /// <summary>
    /// Points for dragging a live trooper to the brain bug, given to whoever was pulling the trooper when the brain was sucked
    /// </summary>
    [DataField]
    public float DeliveryPoints = 15f;

    /// <summary>
    /// Whether this bug gets the action that opens the evolution menu
    /// </summary>
    [DataField]
    public bool CanEvolve = true;

    /// <summary>
    /// Sounds of the bug emotes from the emote wheel: screech, roar, alarm and so on
    /// </summary>
    [DataField]
    public ProtoId<EmoteSoundsPrototype>? EmoteSounds;

    [DataField]
    public SoundSpecifier? DeathSound;

    /// <summary>
    /// Played when the bug gets hurt, at most once per <see cref="PainCooldown"/>
    /// </summary>
    [DataField]
    public SoundSpecifier? PainSound;

    [DataField]
    public TimeSpan PainCooldown = TimeSpan.FromSeconds(1.5);

    [ViewVariables]
    public TimeSpan NextPain;

    /// <summary>
    /// Played where a bug of this caste comes out of its old body
    /// </summary>
    [DataField]
    public SoundSpecifier? EvolveSound = new SoundPathSpecifier("/Audio/_ST14/Mobs/Bugs/hatch.ogg");

    [DataField]
    public EntProtoId EvolveAction = "ST14ActionHiveEvolve";

    [DataField]
    public EntityUid? EvolveActionEntity;
}
