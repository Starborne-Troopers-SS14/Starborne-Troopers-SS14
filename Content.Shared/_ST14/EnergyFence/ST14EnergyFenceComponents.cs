using System.Numerics;
using Content.Shared.DoAfter;
using Content.Shared.Maps;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.EnergyFence;

/// <summary>
/// Fence pylon. Anchored pylons pair up automatically (greedy, nearest pairs first, at most <see cref="MaxLinks"/> each).
/// A link works only while both pylons are powered; it lights up the moment a whitelisted bug enters its capsule zone.
/// All balance numbers live here so they can be tuned from YAML
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14EnergyPylonComponent : Component
{
    /// <summary>
    /// Two pylons link when they are at most this far apart, in tiles
    /// </summary>
    [DataField]
    public float LinkRange = 9f;

    [DataField]
    public int MaxLinks = 2;

    /// <summary>
    /// Radius of the capsule around the link segment where bugs get shocked
    /// </summary>
    [DataField]
    public float ZoneRadius = 2.5f;

    /// <summary>
    /// Shock damage of one hit: <c>BaseDamage * (MinDamageFactor + DepthDamageFactor * depth^2)</c>
    /// </summary>
    [DataField]
    public float BaseDamage = 8f;

    [DataField]
    public float MinDamageFactor = 0.25f;

    [DataField]
    public float DepthDamageFactor = 1.75f;

    /// <summary>
    /// Seconds between hits: <c>BaseInterval - DepthIntervalFactor * depth</c>
    /// </summary>
    [DataField]
    public float BaseInterval = 1f;

    [DataField]
    public float DepthIntervalFactor = 0.67f;

    /// <summary>
    /// Movement speed of a shocked bug: <c>1 - DepthSlowdown * depth</c>
    /// </summary>
    [DataField]
    public float DepthSlowdown = 0.85f;

    /// <summary>
    /// Deeper than this, every hit also paralyzes
    /// </summary>
    [DataField]
    public float StunDepth = 0.8f;

    [DataField]
    public TimeSpan StunTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the arc keeps burning after the last bug left the zone
    /// </summary>
    [DataField]
    public TimeSpan Linger = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A pylon that lost power stays dark at least this long after power comes back
    /// </summary>
    [DataField]
    public TimeSpan PowerUpDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Power draw in watts: <c>IdleLoad + activeLinks * ActiveLoadPerLink + wrappedBugs * PerBugLoad</c>
    /// </summary>
    [DataField]
    public float IdleLoad = 1000f;

    [DataField]
    public float ActiveLoadPerLink = 4000f;

    [DataField]
    public float PerBugLoad = 2000f;

    /// <summary>
    /// Who gets shocked. Empty means every mob
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public SoundSpecifier? HitSound;

    [DataField]
    public EntProtoId? HitEffect = "EffectSparks";

    /// <summary>
    /// A powered idle pylon throws a spark now and then, between these two delays
    /// </summary>
    [DataField]
    public EntProtoId? IdleSparkEffect = "EffectSparks";

    [DataField]
    public TimeSpan IdleSparkMinDelay = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan IdleSparkMaxDelay = TimeSpan.FromSeconds(20);

    [DataField]
    public float IdleLightEnergy = 0.6f;

    [DataField]
    public float ActiveLightEnergy = 2.2f;

    /// <summary>
    /// Where the arc leaves the pylon, relative to its origin (the emitter sits up the tall sprite)
    /// </summary>
    [DataField]
    public Vector2 ArcOffset = new(0f, 1f);

    /// <summary>
    /// Radius of the loop the arc makes around a wrapped bug
    /// </summary>
    [DataField]
    public float LoopRadius = 0.5f;

    /// <summary>
    /// Deeper than this the arc wraps a bug twice
    /// </summary>
    [DataField]
    public float DoubleLoopDepth = 0.6f;

    /// <summary>
    /// Links where this pylon is the first end, with who is wrapped by them. Sent to clients for the arc overlay
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<ST14FenceLinkState> Links = new();

    /// <summary>
    /// Powered and past the power-up delay
    /// </summary>
    [ViewVariables]
    public bool Powered;

    [ViewVariables]
    public TimeSpan? PoweredSince;

    [ViewVariables]
    public int LinkCount;

    [ViewVariables]
    public TimeSpan NextIdleSpark;
}

/// <summary>
/// One link as the clients see it
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14FenceLinkState(NetEntity Other, bool Active, NetEntity[] Wrapped, float[] Depths);

/// <summary>
/// Power panel: takes MV from field generators and feeds LV to the pylons, but only with an energy block inside.
/// The block is the panel's buffer: charged from the surplus, drained when the generators fall short
/// </summary>
[RegisterComponent]
public sealed partial class ST14EnergyBoxComponent : Component
{
    [DataField]
    public string BlockSlot = "energy_block";

    /// <summary>
    /// Main breaker. Off: the panel still charges its block but gives nothing to the pylons
    /// </summary>
    [DataField]
    public bool Breaker = true;

    [DataField]
    public SoundSpecifier? BreakerSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");

    /// <summary>
    /// The block's charge while it sits in the panel. Written back to the block now and then, not every tick
    /// </summary>
    [ViewVariables]
    public float Charge;

    [ViewVariables]
    public EntityUid? Block;

    [ViewVariables]
    public TimeSpan NextFlush;

    [ViewVariables]
    public TimeSpan NextVisuals;
}

/// <summary>
/// Energy block: a big cell that goes into the power panel. Capacity is the block's <c>Battery.maxCharge</c>
/// </summary>
[RegisterComponent]
public sealed partial class ST14EnergyBlockComponent : Component
{
    /// <summary>
    /// Most power the block can give to the pylons through the panel, in watts
    /// </summary>
    [DataField]
    public float MaxOutput = 20000f;

    /// <summary>
    /// Fastest the panel can charge the block, in watts
    /// </summary>
    [DataField]
    public float MaxChargeRate = 8000f;
}

/// <summary>
/// Portable field generator: an MV power supplier toggled by hand
/// </summary>
[RegisterComponent]
public sealed partial class ST14FieldGeneratorComponent : Component
{
    [DataField]
    public bool On = true;

    [DataField]
    public SoundSpecifier? ToggleSound = new SoundPathSpecifier("/Audio/Machines/machine_switch.ogg");
}

/// <summary>
/// On a bug that is inside a working fence zone: slows it down, makes its sprite flicker on the client
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ST14FenceShockedComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public float Depth;

    [ViewVariables, AutoNetworkedField]
    public float SpeedModifier = 1f;
}

/// <summary>
/// Entrenching tool: digs a cable trench in the ground and fills it back in
/// </summary>
[RegisterComponent]
public sealed partial class ST14TrenchToolComponent : Component
{
    /// <summary>
    /// Ground tile → trench tile. Using the tool on a trench fills it back
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<ContentTileDefinition>, ProtoId<ContentTileDefinition>> Trenches = new();

    [DataField]
    public TimeSpan DigTime = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan FillTime = TimeSpan.FromSeconds(1.5);

    [DataField]
    public SoundSpecifier? DigSound;
}

[Serializable, NetSerializable]
public sealed partial class ST14TrenchDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetCoordinates Location;

    /// <summary>
    /// The tile the trench changes into
    /// </summary>
    [DataField]
    public string NewTile = string.Empty;

    private ST14TrenchDoAfterEvent()
    {
    }

    public ST14TrenchDoAfterEvent(NetCoordinates location, string newTile)
    {
        Location = location;
        NewTile = newTile;
    }

    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public enum ST14EnergyPylonVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum ST14EnergyPylonState : byte
{
    Unanchored,
    NoPower,
    Idle,
    Active,
}

[Serializable, NetSerializable]
public enum ST14EnergyBoxVisuals : byte
{
    Breaker,
    HasBlock,
    ChargeLevel,
    Discharging,
}

[Serializable, NetSerializable]
public enum ST14EnergyBlockVisuals : byte
{
    /// <summary>
    /// 0 (empty) to 4 (full)
    /// </summary>
    Level,
}

[Serializable, NetSerializable]
public enum ST14FieldGeneratorVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum ST14FieldGeneratorState : byte
{
    Unanchored,
    Off,
    On,
}
