using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using System.Numerics;
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
    /// Half the size of the blood sprites in tiles: a decal is placed by its corner and turns around its middle.
    /// The server has no sprites to measure, a test compares this with the sprites of the tags above
    /// </summary>
    [DataField]
    public Vector2 DecalCenter = new(0.8125f, 0.8125f);

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
    /// Pools under a body in critical condition
    /// </summary>
    [DataField]
    public int CriticalPools = 1;

    /// <summary>
    /// Pools under a dead body, the ones from its critical condition count
    /// </summary>
    [DataField]
    public int DeathPools = 2;

    /// <summary>
    /// Pools left by a gibbed body, the ones it already left count
    /// </summary>
    [DataField]
    public int GibPools = 3;

    /// <summary>
    /// Splatters thrown around by a gibbed body
    /// </summary>
    [DataField]
    public int GibSplatters = 4;

    /// <summary>
    /// Pools left since the mob went down, starts over when it is back on its feet
    /// </summary>
    [DataField]
    public int Pools;
}

/// <summary>
/// Raised on a mob with <see cref="ST14BloodDecalsComponent"/> when blood leaves its body, in place of spilling a puddle.
/// Burst is set when the whole body empties at once, like when it is gibbed
/// </summary>
[ByRefEvent]
public readonly record struct ST14BleedOutEvent(FixedPoint2 Amount, bool Burst = false);
