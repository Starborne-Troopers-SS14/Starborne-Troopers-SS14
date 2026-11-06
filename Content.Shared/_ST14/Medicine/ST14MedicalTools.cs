using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Medicine;

[Serializable, NetSerializable]
public enum ST14TreatmentKind : byte
{
    /// <summary>
    /// Stops a limb dead, even a stump. Left on too long, the limb dies
    /// </summary>
    Tourniquet,

    /// <summary>
    /// Covers a wound: small bleeding stops, a big one slows
    /// </summary>
    Bandage,

    /// <summary>
    /// Stuffed into a deep wound: holds what a tourniquet cannot reach, a chest, a neck, a cut artery
    /// </summary>
    Hemostatic,

    Splint,

    /// <summary>
    /// Sews the wounds of a part shut for good. Done lying down, and without anaesthesia it is torture
    /// </summary>
    Suture,
}

/// <summary>
/// A medical tool used on a patient. One click treats the part that needs it most, the right-click menu picks the part
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14TreatmentComponent : Component
{
    [DataField(required: true)]
    public ST14TreatmentKind Kind;

    [DataField]
    public float Delay = 2.5f;

    /// <summary>
    /// On yourself it takes longer, with one hand and the pain
    /// </summary>
    [DataField]
    public float SelfDelay = 4f;

    /// <summary>
    /// Stays after use, like a suture kit
    /// </summary>
    [DataField]
    public bool Reusable;

    /// <summary>
    /// The patient has to lie down
    /// </summary>
    [DataField]
    public bool NeedsLying;

    /// <summary>
    /// The pain the treatment itself causes, a tourniquet pulled tight or a needle without anaesthesia
    /// </summary>
    [DataField]
    public float Pain;

    [DataField]
    public SoundSpecifier? StartSound;

    [DataField]
    public SoundSpecifier? EndSound;

    /// <summary>
    /// What a removed tourniquet turns back into
    /// </summary>
    [DataField]
    public EntProtoId? ReturnedItem;
}

[Serializable, NetSerializable]
public sealed partial class ST14TreatDoAfterEvent : DoAfterEvent
{
    [DataField]
    public ST14BodyZone Zone;

    private ST14TreatDoAfterEvent()
    {
    }

    public ST14TreatDoAfterEvent(ST14BodyZone zone)
    {
        Zone = zone;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}

[Serializable, NetSerializable]
public sealed partial class ST14RemoveTourniquetDoAfterEvent : DoAfterEvent
{
    [DataField]
    public ST14BodyZone Zone;

    private ST14RemoveTourniquetDoAfterEvent()
    {
    }

    public ST14RemoveTourniquetDoAfterEvent(ST14BodyZone zone)
    {
        Zone = zone;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}

[Serializable, NetSerializable]
public sealed partial class ST14CprDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// The field scanner of a medic: shows a patient part by part, the heart, the blood and the drugs in it
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14MedScannerComponent : Component
{
    [ViewVariables]
    public EntityUid? Patient;

    [ViewVariables]
    public TimeSpan NextUpdate;

    [DataField]
    public float Range = 2.5f;

    [DataField]
    public SoundSpecifier? ScanSound = new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");
}

[Serializable, NetSerializable]
public enum ST14MedScannerUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public readonly record struct ST14DrugReading(string Reagent, float Amount, float Overdose, float Strength);

[Serializable, NetSerializable]
public sealed class ST14MedScannerState(
    NetEntity patient,
    string name,
    ST14Wound[] wounds,
    Dictionary<ST14BodyZone, float> tourniquets,
    ST14BodyZone[] missing,
    float pain,
    int pulse,
    ST14HeartState heart,
    float blood,
    float bleeding,
    float heartLoad,
    ST14DrugReading[] drugs,
    string[] warnings,
    byte mobState,
    float ischemiaTime,
    float necrosisTime,
    bool inRange) : BoundUserInterfaceState
{
    public readonly NetEntity Patient = patient;
    public readonly string Name = name;
    public readonly ST14Wound[] Wounds = wounds;
    public readonly Dictionary<ST14BodyZone, float> Tourniquets = tourniquets;
    public readonly ST14BodyZone[] Missing = missing;
    public readonly float Pain = pain;
    public readonly int Pulse = pulse;
    public readonly ST14HeartState Heart = heart;
    public readonly float Blood = blood;
    public readonly float Bleeding = bleeding;
    public readonly float HeartLoad = heartLoad;
    public readonly ST14DrugReading[] Drugs = drugs;
    public readonly string[] Warnings = warnings;

    /// <summary>
    /// 0 alive, 1 critical, 2 dead
    /// </summary>
    public readonly byte MobState = mobState;

    public readonly float IschemiaTime = ischemiaTime;
    public readonly float NecrosisTime = necrosisTime;
    public readonly bool InRange = inRange;
}

/// <summary>
/// Every trooper can shout for a medic: the medics see a red cross over them for a minute
/// </summary>
[RegisterComponent]
public sealed partial class ST14MedicCallerComponent : Component
{
    [DataField]
    public EntProtoId Action = "ST14ActionCallMedic";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(60);
}

public sealed partial class ST14CallMedicActionEvent : InstantActionEvent;
