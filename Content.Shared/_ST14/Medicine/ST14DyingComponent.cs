using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// A person down in crit. The body runs out of air beat by beat, and the player fights for it by pressing in time
/// with the heart: a press on the beat slows the dying, a press off it speeds it up, no press changes nothing
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14DyingComponent : Component
{
    /// <summary>
    /// Damage per second of a heart nobody is fighting for
    /// </summary>
    [DataField]
    public float DamagePerSecond = 0.75f;

    [DataField]
    public float HitMultiplier = 0.6f;

    [DataField]
    public float MissMultiplier = 1.5f;

    /// <summary>
    /// Seconds between beats of a fresh wound, the heart slows down to <see cref="SlowestInterval"/> near death
    /// </summary>
    [DataField]
    public float FastestInterval = 1f;

    [DataField]
    public float SlowestInterval = 1.6f;

    /// <summary>
    /// How many seconds a press may be off the beat. Wide, for players with a high ping
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Window = 0.22f;

    [DataField, AutoNetworkedField]
    public float ReviveWindow = 0.35f;

    /// <summary>
    /// Brute damage healed when the player catches the beat right after a defibrillator shock
    /// </summary>
    [DataField]
    public float ReviveHeal = 40f;

    [DataField]
    public SoundSpecifier HitSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_ready.ogg");

    [AutoNetworkedField, ViewVariables]
    public TimeSpan NextBeat;

    [AutoNetworkedField, ViewVariables]
    public float BeatInterval = 1f;

    /// <summary>
    /// The next beat is the one big beat after a shock
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public bool ReviveBeat;

    /// <summary>
    /// How the player did on the next beat so far
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public ST14BeatResult Result;
}

[Serializable, NetSerializable]
public enum ST14BeatResult : byte
{
    None,
    Hit,
    Miss,
}

/// <summary>
/// The player pressed the heartbeat key
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14HeartbeatPressEvent : EntityEventArgs;
