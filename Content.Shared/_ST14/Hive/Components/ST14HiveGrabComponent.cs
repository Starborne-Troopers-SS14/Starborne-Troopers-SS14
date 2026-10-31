using System.Numerics;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Grip of the hive: the bug leaps at a trooper, knocks it down and carries it off in its claws to the brain bug.
/// Shots meant for the trooper hit the bug, and enough of them make it let go
/// </summary>
[RegisterComponent]
public sealed partial class ST14HiveGrabComponent : Component
{
    /// <summary>
    /// Damage the bug takes before it lets go
    /// </summary>
    [DataField]
    public FixedPoint2 BreakDamage = 40;

    /// <summary>
    /// How close the brain bug has to be for the delivery to start the sucking, in tiles
    /// </summary>
    [DataField]
    public float DeliveryRange = 2.2f;

    /// <summary>
    /// How far from the trooper the bug may land and still catch it, in tiles
    /// </summary>
    [DataField]
    public float CatchRange = 1.3f;

    [DataField]
    public float Speed = 12f;

    /// <summary>
    /// Where the trooper hangs in the claws, in front of the bug
    /// </summary>
    [DataField]
    public Vector2 HoldOffset = new(0f, -0.5f);

    /// <summary>
    /// Cooldown after the trooper is lost, and after a leap that caught nothing
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan MissCooldown = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long the trooper stays down once it is dropped
    /// </summary>
    [DataField]
    public TimeSpan DropKnockdown = TimeSpan.FromSeconds(1.5);

    [DataField]
    public SoundSpecifier? LeapSound;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public SoundSpecifier? ReleaseSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBugGrab";

    [DataField]
    public EntityUid? ActionEntity;

    /// <summary>
    /// The trooper in the claws right now
    /// </summary>
    [DataField]
    public EntityUid? Victim;

    /// <summary>
    /// Damage taken since the grab started
    /// </summary>
    [DataField]
    public FixedPoint2 DamageTaken;
}

/// <summary>
/// A bug in the middle of its leap at the trooper it wants to grab
/// </summary>
[RegisterComponent]
public sealed partial class ST14HiveGrabLeapComponent : Component
{
    [DataField]
    public EntityUid Target;
}

/// <summary>
/// A trooper held in the claws of a bug: no shooting, no walking, only struggling. Keys to press pop up on its screen,
/// any key helps a little, the shown one more, and hitting it right as the ring closes most of all
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ST14HiveGrabbedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Grabber;

    /// <summary>
    /// Struggle built up so far. The trooper is free once it reaches <see cref="StruggleNeeded"/>
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Struggle;

    [DataField, AutoNetworkedField]
    public float StruggleNeeded = 20f;

    /// <summary>
    /// Struggle lost every second, so the trooper has to keep fighting
    /// </summary>
    [DataField]
    public float StruggleDecay = 1.5f;

    /// <summary>
    /// Struggle for any key, for the shown key and for the shown key right on time
    /// </summary>
    [DataField]
    public float MashGain = 0.4f;

    [DataField]
    public float HitGain = 1.5f;

    [DataField]
    public float PerfectGain = 4f;

    /// <summary>
    /// The key the trooper is asked to press now
    /// </summary>
    [DataField, AutoNetworkedField]
    public MoveButtons Prompt = MoveButtons.Up;

    /// <summary>
    /// When the ring around the shown key closes, the perfect moment to press it
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan PromptPerfect;

    /// <summary>
    /// From a new key showing up to its perfect moment
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan PromptLead = TimeSpan.FromSeconds(0.9);

    /// <summary>
    /// How far from the perfect moment a press still counts as perfect
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan PerfectWindow = TimeSpan.FromSeconds(0.15);

    /// <summary>
    /// A key nobody pressed is replaced this long after its perfect moment
    /// </summary>
    [DataField]
    public TimeSpan PromptLinger = TimeSpan.FromSeconds(0.5);

    [DataField, AutoPausedField]
    public TimeSpan NextSync;
}
