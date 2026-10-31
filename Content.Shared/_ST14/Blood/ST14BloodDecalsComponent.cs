using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Blood;

/// <summary>
/// Blood leaves decals on the floor instead of puddles: a splatter thrown away from every hard enough hit,
/// drops while the mob bleeds and a pool under the body when it goes down
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ST14BloodDecalsComponent : Component
{
    [DataField]
    public string SplatterTag = "ST14BloodSplatter";

    [DataField]
    public string DripTag = "ST14BloodDrip";

    [DataField]
    public string PoolTag = "ST14BloodPool";

    /// <summary>
    /// Damage types that spill blood on a hit and how much each one counts
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> SplatterDamage = new()
    {
        { "Slash", 1f },
        { "Piercing", 1f },
        { "Blunt", 0.5f },
    };

    /// <summary>
    /// A hit weaker than this leaves no splatter
    /// </summary>
    [DataField]
    public float SplatterThreshold = 5f;

    /// <summary>
    /// A hit this strong throws a second splatter further away
    /// </summary>
    [DataField]
    public float HeavySplatterThreshold = 20f;

    /// <summary>
    /// Shortest time between two splatters, so a burst of fire does not carpet the floor
    /// </summary>
    [DataField]
    public TimeSpan SplatterCooldown = TimeSpan.FromSeconds(0.15);

    [DataField, AutoPausedField]
    public TimeSpan NextSplatter;

    /// <summary>
    /// Blood lost while bleeding that makes one drop on the floor
    /// </summary>
    [DataField]
    public FixedPoint2 DripVolume = 3;

    /// <summary>
    /// Blood lost since the last drop
    /// </summary>
    [DataField]
    public FixedPoint2 Lost;

    /// <summary>
    /// How far from the mob a drop may land, in tiles
    /// </summary>
    [DataField]
    public float DripSpread = 0.35f;

    /// <summary>
    /// Pools left under the body when it dies
    /// </summary>
    [DataField]
    public int DeathPools = 2;
}

/// <summary>
/// Raised on a mob with <see cref="ST14BloodDecalsComponent"/> when blood leaves its body, in place of spilling a puddle.
/// Burst is set when the whole body empties at once, like when it is gibbed
/// </summary>
[ByRefEvent]
public readonly record struct ST14BleedOutEvent(FixedPoint2 Amount, bool Burst = false);
