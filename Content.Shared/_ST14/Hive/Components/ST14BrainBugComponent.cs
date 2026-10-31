using Content.Shared.Roles;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// The brain bug. It sucks the brains of live troopers for hive points and spends them to open new tiers and species
/// </summary>
[RegisterComponent]
public sealed partial class ST14BrainBugComponent : Component
{
    [DataField]
    public float SuckTime = 8f;

    /// <summary>
    /// A brain is being sucked out right now
    /// </summary>
    [DataField]
    public bool Sucking;

    [DataField]
    public int RegularMin = 15;

    [DataField]
    public int RegularMax = 25;

    [DataField]
    public int CommandMin = 50;

    [DataField]
    public int CommandMax = 70;

    /// <summary>
    /// Jobs whose brains count as command
    /// </summary>
    [DataField]
    public HashSet<ProtoId<JobPrototype>> CommandJobs = new();

    /// <summary>
    /// Departments whose jobs count as command
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DepartmentPrototype>> CommandDepartments = new() { "Command" };

    /// <summary>
    /// Played when the proboscis goes in, while the brain is being sucked
    /// </summary>
    [DataField]
    public SoundSpecifier? SuckStartSound;

    /// <summary>
    /// Played when the brain is gone
    /// </summary>
    [DataField]
    public SoundSpecifier? SuckSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBrainSuck";

    [DataField]
    public EntityUid? ActionEntity;
}
