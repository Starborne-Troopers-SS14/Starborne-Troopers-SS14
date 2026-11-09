using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// What a reagent in the blood does to a wounded body. Every effect is given at full strength, the amount of
/// <see cref="FullDose"/>; less of it works in proportion. Past <see cref="Overdose"/> it starts to harm.
/// Read by the wound system every second, so a new drug is one entry in YAML
/// </summary>
[Prototype("st14Drug")]
public sealed partial class ST14DrugPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>
    /// The amount in the blood at which it works in full
    /// </summary>
    [DataField]
    public float FullDose = 10f;

    /// <summary>
    /// The amount past which it harms, 0 for never
    /// </summary>
    [DataField]
    public float Overdose;

    /// <summary>
    /// A few words for the scanner on what it does
    /// </summary>
    [DataField]
    public LocId? Summary;

    /// <summary>
    /// Pain taken away, out of 100. Negative adds pain
    /// </summary>
    [DataField]
    public float PainRelief;

    // the states it puts the body in, from 0 to 1: the screen, the walk and the aim follow them

    [DataField]
    public float Sedation;

    [DataField]
    public float Stimulation;

    [DataField]
    public float Adrenergic;

    [DataField]
    public float Dissociation;

    [DataField]
    public float Toxin;

    [DataField]
    public float HeartLoad;

    [DataField]
    public float Pulse;

    /// <summary>
    /// More blood out of every open wound, 0.3 is a third more; negative holds it back
    /// </summary>
    [DataField]
    public float Bleeding;

    /// <summary>
    /// Bleeding taken off the small wounds every second on top of their own clotting
    /// </summary>
    [DataField]
    public float Clotting;

    /// <summary>
    /// Degrees taken off the shaking of the aim
    /// </summary>
    [DataField]
    public float Steady;

    /// <summary>
    /// Blood added every second
    /// </summary>
    [DataField]
    public float BloodRestore;

    /// <summary>
    /// How many times faster the open wounds close
    /// </summary>
    [DataField]
    public float Regeneration;

    /// <summary>
    /// How much of a fracture grows back every second
    /// </summary>
    [DataField]
    public float BoneGrowth;

    /// <summary>
    /// Infection taken out of every wound every second
    /// </summary>
    [DataField]
    public float AntiInfection;

    /// <summary>
    /// The part of the fever the body stops feeling. The infection goes on
    /// </summary>
    [DataField]
    public float FeverRelief;

    /// <summary>
    /// Seconds of a stimulant crash taken off every second
    /// </summary>
    [DataField]
    public float CrashRelief;

    /// <summary>
    /// Seconds of crash for every unit of the highest dose, once it wears off
    /// </summary>
    [DataField]
    public float CrashAfter;

    /// <summary>
    /// Keeps the heart in rhythm, from 0 to 1: an overload turns into arrhythmia slower and stops it less often
    /// </summary>
    [DataField]
    public float RhythmGuard;

    /// <summary>
    /// Extra chance for chest compressions and a defibrillator to start a stopped heart
    /// </summary>
    [DataField]
    public float RestartBonus;

    /// <summary>
    /// Other reagents it pulls out of the blood, units per second at full strength: an antidote
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ReagentPrototype>, float> Removes = new();

    /// <summary>
    /// Asphyxiation every second past the overdose, a tenth more for every unit beyond it
    /// </summary>
    [DataField]
    public float OverdoseBreathing;

    /// <summary>
    /// Damage every second past the overdose
    /// </summary>
    [DataField]
    public DamageSpecifier? OverdoseDamage;

    [DataField]
    public float OverdosePulse;

    /// <summary>
    /// Chance every second past the overdose that the heart stops
    /// </summary>
    [DataField]
    public float OverdoseArrest;

    [DataField]
    public LocId? OverdosePopup;

    /// <summary>
    /// Shown when it wears off and the crash comes
    /// </summary>
    [DataField]
    public LocId? WearOffPopup;
}

/// <summary>
/// What two or more drugs do together when all of them work at least at <see cref="MinStrength"/>: a heart overloaded
/// past what each would do alone, breathing that stops, drugs that cancel each other out
/// </summary>
[Prototype("st14DrugInteraction")]
public sealed partial class ST14DrugInteractionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<ProtoId<ST14DrugPrototype>> Drugs = new();

    [DataField]
    public float MinStrength = 0.25f;

    [DataField]
    public float HeartLoad;

    [DataField]
    public float Pulse;

    /// <summary>
    /// Asphyxiation every second
    /// </summary>
    [DataField]
    public float Breathing;

    [DataField]
    public float Bleeding;

    [DataField]
    public float Clotting;

    [DataField]
    public float Sedation;

    [DataField]
    public float Dissociation;

    [DataField]
    public float PainRelief;

    /// <summary>
    /// Chance every second that the heart stops
    /// </summary>
    [DataField]
    public float Arrest;

    /// <summary>
    /// Drugs that work weaker together, by this factor
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<ST14DrugPrototype>, float> Weakens = new();

    /// <summary>
    /// What the scanner warns about
    /// </summary>
    [DataField]
    public LocId? Warning;

    /// <summary>
    /// What the patient feels now and then
    /// </summary>
    [DataField]
    public LocId? Popup;
}
