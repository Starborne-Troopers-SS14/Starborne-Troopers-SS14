using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// A larva eating corpses: every bite stores a unit of the biomass it needs to evolve
/// </summary>
[RegisterComponent]
public sealed partial class ST14BiomassEaterComponent : Component
{
    /// <summary>
    /// Seconds per unit of biomass
    /// </summary>
    [DataField]
    public float BiteTime = 1.5f;

    [DataField]
    public SoundSpecifier? BiteSound = new SoundPathSpecifier("/Audio/_ST14/Mobs/Bugs/eat.ogg");

    [DataField]
    public EntProtoId Action = "ST14ActionEatCorpse";

    [DataField]
    public EntityUid? ActionEntity;
}
