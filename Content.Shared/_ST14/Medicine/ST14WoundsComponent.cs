using Robust.Shared.GameStates;
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
    /// How bad it is, from 0 to 1: the pain and the look of it
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

    public ST14Wound Copy()
    {
        return (ST14Wound) MemberwiseClone();
    }
}

/// <summary>
/// The wounds of a body part by part, the tourniquets on it, and how the body takes them: pain, the heart and the
/// drugs that work in the blood. The server keeps it, the client draws the screen, the sounds and the marks on the
/// body from it
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

    /// <summary>
    /// How hard each drug works right now, from 0 to 1, for the screen and the scanner
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Morphine;

    [DataField, AutoNetworkedField]
    public float Stim;

    [DataField, AutoNetworkedField]
    public float Adrenaline;

    [DataField, AutoNetworkedField]
    public float Ketamine;

    /// <summary>
    /// The crash after a stimulant, from 0 to 1
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Crash;

    /// <summary>
    /// How overloaded the heart is, from 0 to 1. Kept high it goes out of rhythm and stops
    /// </summary>
    [DataField, AutoNetworkedField]
    public float HeartLoad;

    // what the server tracks between the ticks, kept in the save of the body

    [DataField]
    public float CrashLeft;

    [DataField]
    public float StimDose;

    [DataField]
    public float ArrhythmiaTime;

    /// <summary>
    /// A fresh hit hurts more for a while: it fades by itself
    /// </summary>
    [DataField]
    public float PainSpike;

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
