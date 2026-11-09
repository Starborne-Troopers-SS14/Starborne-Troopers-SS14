using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// A part of the body a wound sits on and a tourniquet, bandage or splint goes on
/// </summary>
[Serializable, NetSerializable]
public enum ST14BodyZone : byte
{
    Head,
    Torso,
    ArmLeft,
    ArmRight,
    LegLeft,
    LegRight,
}

[Serializable, NetSerializable]
public enum ST14WoundKind : byte
{
    /// <summary>
    /// Claws and blades: bleeds
    /// </summary>
    Cut,

    /// <summary>
    /// Bullets and spikes: bleeds from deep inside
    /// </summary>
    Puncture,

    Burn,

    /// <summary>
    /// A broken bone of an arm or a leg: hurts, a leg limps and an arm shakes until it is splinted
    /// </summary>
    Fracture,

    /// <summary>
    /// What is left of a torn-off limb: an artery pumping blood out until a tourniquet stops it
    /// </summary>
    Stump,

    /// <summary>
    /// A bone that grew back crooked without a splint: a limp or a shaking aim for good, until it is broken again
    /// and set right with the suture kit
    /// </summary>
    Malunion,
}

[Serializable, NetSerializable]
public enum ST14HeartState : byte
{
    Normal,

    /// <summary>
    /// Fast from pain, blood loss or drugs
    /// </summary>
    Racing,

    /// <summary>
    /// Overloaded and beating out of rhythm: it can stop any moment
    /// </summary>
    Arrhythmia,

    /// <summary>
    /// Stopped. The body suffocates until CPR, adrenaline or a defibrillator gets it going
    /// </summary>
    Arrest,
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class ST14Wound
{
    [DataField]
    public ST14WoundKind Kind;

    [DataField]
    public ST14BodyZone Zone;

    /// <summary>
    /// How bad it is, from 0 to 1: the pain and the look of it. For a fracture, how far it is from grown back
    /// </summary>
    [DataField]
    public float Severity;

    /// <summary>
    /// Blood it lets out every second when nothing holds it
    /// </summary>
    [DataField]
    public float Bleed;

    /// <summary>
    /// An artery is cut: it bleeds hard, a bandage alone does not hold it
    /// </summary>
    [DataField]
    public bool Arterial;

    [DataField]
    public bool Bandaged;

    /// <summary>
    /// Stuffed with hemostatic gauze: what a tourniquet cannot reach, a chest or a neck
    /// </summary>
    [DataField]
    public bool Packed;

    /// <summary>
    /// Sewn up by a medic: it does not bleed any more and hurts far less
    /// </summary>
    [DataField]
    public bool Sutured;

    [DataField]
    public bool Splinted;

    /// <summary>
    /// Washed with antiseptic: it does not get infected while it stays closed
    /// </summary>
    [DataField]
    public bool Clean;

    /// <summary>
    /// Made by bug claws: it gets infected twice as fast
    /// </summary>
    [DataField]
    public bool Dirty;

    /// <summary>
    /// How far the wound has gone bad, from 0 to 1. It burns with fever past a third, it poisons the blood past 0.8
    /// </summary>
    [DataField]
    public float Infection;

    public ST14Wound Copy()
    {
        return (ST14Wound) MemberwiseClone();
    }
}

/// <summary>
/// The wounds of a body part by part, the tourniquets on it, and how the body takes them: pain, the heart, infection
/// and the drugs that work in the blood. The server keeps it, the client draws the screen, the sounds and the marks
/// on the body from it
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ST14WoundsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ST14Wound> Wounds = new();

    /// <summary>
    /// The limbs under a tourniquet and how many seconds it has been on. Past <see cref="IschemiaTime"/> the limb
    /// starts to die, past <see cref="NecrosisTime"/> it is lost
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<ST14BodyZone, float> Tourniquets = new();

    /// <summary>
    /// What the body feels, from 0 to 100, after the painkillers
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Pain;

    [DataField, AutoNetworkedField]
    public int Pulse = 72;

    [DataField, AutoNetworkedField]
    public ST14HeartState Heart;

    /// <summary>
    /// Blood left, 1 is full
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Blood = 1f;

    /// <summary>
    /// Blood lost every second right now
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Bleeding;

    // what the drugs do to the body right now, each from 0 to 1, for the screen, the walk, the aim and the scanner

    /// <summary>
    /// The warm haze of morphine and alcohol
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Sedation;

    [DataField, AutoNetworkedField]
    public float Stim;

    [DataField, AutoNetworkedField]
    public float Adrenaline;

    /// <summary>
    /// Ketamine: the world floats away, past 0.6 the body is under anaesthesia
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Dissociation;

    /// <summary>
    /// Bug venom: heavy limbs, a green sick picture, the breath giving out
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Toxin;

    /// <summary>
    /// The crash after a stimulant
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Crash;

    /// <summary>
    /// The fever the body feels: the infection, less what a fever reducer hides
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Fever;

    /// <summary>
    /// The worst infection of all the wounds, from 0 to 1, whatever the fever reducer hides
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Infection;

    /// <summary>
    /// Degrees the drugs take off the shaking of the aim
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Steady;

    /// <summary>
    /// How overloaded the heart is, from 0 to 1. Kept high it goes out of rhythm and stops
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HeartLoad;

    // what the server tracks between the ticks, kept in the save of the body

    [DataField]
    public float CrashLeft;

    /// <summary>
    /// The highest dose of each drug that crashes when it wears off, since it was taken
    /// </summary>
    [DataField]
    public Dictionary<string, float> CrashPeaks = new();

    [DataField]
    public float ArrhythmiaTime;

    /// <summary>
    /// A fresh hit hurts more for a while: it fades by itself
    /// </summary>
    [DataField]
    public float PainSpike;

    /// <summary>
    /// Pain the drugs take away right now
    /// </summary>
    [ViewVariables]
    public float Relief;

    /// <summary>
    /// Extra chance for chest compressions and a defibrillator to start the heart, from the drugs in the blood
    /// </summary>
    [ViewVariables]
    public float RestartBonus;

    /// <summary>
    /// The drug interactions going on in the blood right now, for the scanner
    /// </summary>
    [ViewVariables]
    public List<ProtoId<ST14DrugInteractionPrototype>> Interactions = new();

    [ViewVariables]
    public TimeSpan NextTick;

    [ViewVariables]
    public TimeSpan NextVoice;

    [ViewVariables]
    public TimeSpan NextCollapse;

    [ViewVariables]
    public int SpeedKey = -1;

    [ViewVariables]
    public int AimKey = -1;

    [DataField]
    public float IschemiaTime = 300f;

    [DataField]
    public float NecrosisTime = 600f;
}

/// <summary>
/// A medic that sees the wounded on the HUD: who bleeds, who has a tourniquet on, whose heart stopped
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14MedicHudComponent : Component;

/// <summary>
/// A trooper calling for a medic: the medics see a red cross over them for a while
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14CallingMedicComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan Until;
}

/// <summary>
/// A bug whose claws carry venom: now and then a hit puts some into the blood
/// </summary>
[RegisterComponent]
public sealed partial class ST14VenomousComponent : Component
{
    [DataField]
    public string Reagent = "ST14BugVenom";

    [DataField]
    public float Chance = 0.3f;

    [DataField]
    public float Amount = 3f;

    /// <summary>
    /// The claws do not put more venom in a body that already has this much
    /// </summary>
    [DataField]
    public float Max = 15f;
}

/// <summary>
/// The moment a body is torn or shocked: the victim's screen goes white and the ears ring
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14MedicalShockEvent(float strength) : EntityEventArgs
{
    public readonly float Strength = strength;
}

public static class ST14Zones
{
    public static readonly ST14BodyZone[] All =
    {
        ST14BodyZone.Head, ST14BodyZone.Torso, ST14BodyZone.ArmLeft, ST14BodyZone.ArmRight, ST14BodyZone.LegLeft, ST14BodyZone.LegRight,
    };

    public static bool IsLimb(ST14BodyZone zone)
    {
        return zone is ST14BodyZone.ArmLeft or ST14BodyZone.ArmRight or ST14BodyZone.LegLeft or ST14BodyZone.LegRight;
    }

    public static bool IsLeg(ST14BodyZone zone)
    {
        return zone is ST14BodyZone.LegLeft or ST14BodyZone.LegRight;
    }

    public static bool IsArm(ST14BodyZone zone)
    {
        return zone is ST14BodyZone.ArmLeft or ST14BodyZone.ArmRight;
    }

    /// <summary>
    /// A wound that opens the skin: it bleeds, it can be dressed, washed and sewn, and it can go bad
    /// </summary>
    public static bool IsOpen(ST14WoundKind kind)
    {
        return kind is ST14WoundKind.Cut or ST14WoundKind.Puncture or ST14WoundKind.Burn or ST14WoundKind.Stump;
    }

    /// <summary>
    /// The organ category of the limb, for the limbs that can be torn off and replaced
    /// </summary>
    public static string? Category(ST14BodyZone zone)
    {
        return zone switch
        {
            ST14BodyZone.ArmLeft => "ArmLeft",
            ST14BodyZone.ArmRight => "ArmRight",
            ST14BodyZone.LegLeft => "LegLeft",
            ST14BodyZone.LegRight => "LegRight",
            _ => null,
        };
    }

    public static string Name(ST14BodyZone zone)
    {
        return Loc.GetString($"st14-zone-{zone.ToString().ToLowerInvariant()}");
    }
}
