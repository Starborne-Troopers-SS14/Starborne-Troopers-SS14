using Content.Shared.Radio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Comms;

/// <summary>
/// The radio of the Mobile Infantry, built into the helmet of every trooper. ";" and ":h" speak to the squad, ":r" to
/// the Rodger Young. The squad only hears it near a signaller with a radio pack, the Rodger Young only while a comms
/// tower on the planet holds its frequency
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14SquadRadioComponent : Component
{
    public static readonly ProtoId<RadioChannelPrototype> SquadChannel = "ST14Squad";
    public static readonly ProtoId<RadioChannelPrototype> RodgerChannel = "ST14Rodger";
}

/// <summary>
/// The signaller: the only one who can catch the frequency of the Rodger Young on a comms tower
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14SignallerComponent : Component;

/// <summary>
/// Frequencies of the field radio, kept in tenths of a megahertz: 300 is 30.0 MHz
/// </summary>
public static class ST14Frequency
{
    public const int Min = 300;
    public const int Max = 900;

    public static string Format(int tenths)
    {
        return $"{tenths / 10}.{tenths % 10}";
    }
}

/// <summary>
/// The radio pack of the signaller. Worn on the back of somebody alive it carries the squad radio of the squad of the
/// wearer in a circle around them, and lets the squad see each other on the battle map there. Set to the frequency a
/// comms tower holds, it also carries the Rodger Young
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14RadioPackComponent : Component
{
    /// <summary>
    /// How far from the wearer the radio carries at each power of the transmitter, in tiles: low, medium, high
    /// </summary>
    [DataField]
    public float[] Ranges = { 25f, 40f, 60f };

    /// <summary>
    /// The power of the transmitter, an index into the ranges. At the highest the bugs pick the pack up on their map
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Power = 1;

    /// <summary>
    /// Switched off the pack carries nothing and its light is dark
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Powered = true;

    /// <summary>
    /// Frequencies kept in the memory of the pack, in tenths of a megahertz
    /// </summary>
    [DataField, AutoNetworkedField]
    public int[] Presets = { 600, 600, 600 };

    public float Range => Ranges[Math.Clamp(Power, 0, Ranges.Length - 1)];

    /// <summary>
    /// Who carries it on the back, if anyone
    /// </summary>
    [AutoNetworkedField]
    public EntityUid? Wearer;

    /// <summary>
    /// The frequency the pack is set to, in tenths of a megahertz
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Frequency = 600;
}

[Serializable, NetSerializable]
public enum ST14RadioPackVisuals : byte
{
    Active,
}

/// <summary>
/// A comms tower. A signaller searches the frequency of the Rodger Young on its tuner; once caught, every radio pack set
/// to that frequency carries the channel ":r" of all squads of the planet. The frequency drifts away: some minutes later
/// the signal fades, the words break up, and at last it is gone until a signaller catches a new one
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14CommsTowerComponent : Component
{
    /// <summary>
    /// How long the frequency holds clean after it was caught
    /// </summary>
    [DataField]
    public TimeSpan HoldTime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long the signal fades after that until it is gone
    /// </summary>
    [DataField]
    public TimeSpan FadeTime = TimeSpan.FromMinutes(4);

    /// <summary>
    /// How long the dial has to stay on the frequency to catch it, in seconds
    /// </summary>
    [DataField]
    public float LockTime = 3f;

    /// <summary>
    /// Troopers this close to the tower reach it without a radio pack, in tiles
    /// </summary>
    [DataField]
    public float Range = 12f;

    /// <summary>
    /// When the frequency was caught last, null when it never was
    /// </summary>
    [ViewVariables]
    public TimeSpan? CaughtAt;

    /// <summary>
    /// The frequency the tower holds, in tenths of a megahertz
    /// </summary>
    [ViewVariables]
    public int Frequency;

    /// <summary>
    /// Whether the tower already told the weakening and the loss of the signal
    /// </summary>
    [ViewVariables]
    public bool WarnedFade, WarnedLost;

    // the search in progress: who tunes, where the dial stands, where the frequency is hidden, how far the catch went
    [ViewVariables]
    public EntityUid? Tuner;

    [ViewVariables]
    public int Dial, Target;

    [ViewVariables]
    public float Lock;
}

[Serializable, NetSerializable]
public enum ST14TunerUiKey : byte
{
    Key,
}

/// <summary>
/// What the tuner shows. On a tower: how strong the hidden frequency sounds where the dial stands and how far the catch
/// went. On a radio pack: the frequency of the pack and whether a tower holds it
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14TunerState(bool pack, int dial, float signal, float lockProgress, float quality, int? towerFrequency,
    bool powered = true, int power = 1, float range = 0f, int[]? presets = null)
    : BoundUserInterfaceState
{
    // the settings of a radio pack
    public readonly bool Powered = powered;
    public readonly int Power = power;
    public readonly float Range = range;
    public readonly int[] Presets = presets ?? Array.Empty<int>();

    public readonly bool Pack = pack;
    public readonly int Dial = dial;
    public readonly float Signal = signal;
    public readonly float Lock = lockProgress;

    /// <summary>
    /// How clean the frequency of the Rodger Young holds, 0 when no tower holds one
    /// </summary>
    public readonly float Quality = quality;

    /// <summary>
    /// The frequency a tower holds, known to the signaller: shown on the tuner of the tower and of the pack
    /// </summary>
    public readonly int? TowerFrequency = towerFrequency;
}

/// <summary>
/// The signaller set the dial of the tuner, in tenths of a megahertz
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14TunerDialMessage(int dial) : BoundUserInterfaceMessage
{
    public readonly int Dial = dial;
}

/// <summary>
/// The signaller switched the radio pack on or off
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14RadioPackPowerMessage(bool powered) : BoundUserInterfaceMessage
{
    public readonly bool Powered = powered;
}

/// <summary>
/// The signaller set the power of the transmitter of the radio pack
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14RadioPackRangeMessage(int power) : BoundUserInterfaceMessage
{
    public readonly int Power = power;
}

/// <summary>
/// The signaller put the dial into a memory cell of the pack, or took a frequency out of it
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14RadioPackPresetMessage(int slot, bool save) : BoundUserInterfaceMessage
{
    public readonly int Slot = slot;
    public readonly bool Save = save;
}

[Serializable, NetSerializable]
public enum ST14CommsTowerVisuals : byte
{
    Online,
}
