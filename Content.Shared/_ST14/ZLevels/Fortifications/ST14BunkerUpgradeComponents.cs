/*
 * Starborne Troopers bunker upgrades on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.ZLevels.Fortifications;

/// <summary>
/// What a bunker (one 3x3 dug-out) is upgraded into. As in Foxhole, an upgrade is built on top of the bunker: it stands
/// on its roof, over the middle, and is what the bunker looks like from outside. The inside stays free to walk
/// through. One upgrade a bunker; the engineer builds it with the multitool, from inside or from the roof
/// (<c>ST14DugoutUpgradeCondition</c>).
/// </summary>
[Serializable, Robust.Shared.Serialization.NetSerializable]
public enum ST14BunkerUpgradeKind : byte
{
    /// <summary>
    /// A forward base: troopers come back to life in it, and keep their things in its supply crate
    /// </summary>
    Base,

    /// <summary>
    /// A rifle post: fires at the bugs round the bunker by itself, every way
    /// </summary>
    RifleGarrison,

    /// <summary>
    /// A machine gun nest: further and faster than the rifles, but only within its arc
    /// </summary>
    MachineGunGarrison,

    /// <summary>
    /// A heavy gun: only at the big bugs, slow and hard, within its arc
    /// </summary>
    AntiLargeGarrison,

    /// <summary>
    /// A radio tower: whoever is inside the bunker sees further. Only looks, does not shoot.
    /// </summary>
    Observation,

    /// <summary>
    /// A ramp down into the bunker from the ground next to it
    /// </summary>
    Ramp,
}

/// <summary>
/// An upgrade on the roof of a bunker, over its middle. Built inside, it moves up onto the roof
/// (<c>ST14BunkerUpgradeSystem</c>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14BunkerUpgradeComponent : Component
{
    [DataField(required: true)]
    public ST14BunkerUpgradeKind Kind;

    /// <summary>
    /// What it puts inside the bunker, in a corner out of the way (the supply crate of the base). Taken down, it goes
    /// with it.
    /// </summary>
    [DataField]
    public EntProtoId? Inside;

    [DataField]
    public EntityUid? InsideEntity;
}

/// <summary>
/// Something on the roof of a bunker (an upgrade, the gun of a garrison): it fades and goes with the roof when the
/// player is in the trench line or under the roof
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerTopComponent : Component;

/// <summary>
/// A garrison: it puts its gun (<see cref="Turret"/>) in the middle of its roof, where it fires at the bugs; taken down,
/// the gun goes with it
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerGarrisonComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Turret;

    [DataField]
    public EntityUid? Gun;
}

/// <summary>
/// The gun of a garrison, on the roof of its bunker. It looks for the nearest bug it can see within
/// <see cref="Range"/> and within <see cref="Arc"/> of <see cref="Facing"/>, turns to it and fires
/// (<c>ST14BunkerGarrisonGunSystem</c>). Its bullets go out through the roof of its own bunker.
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerGarrisonGunComponent : Component
{
    [DataField]
    public float Range = 10f;

    /// <summary>
    /// How wide it turns, in degrees: 360 fires every way
    /// </summary>
    [DataField]
    public float Arc = 360f;

    /// <summary>
    /// The middle of its arc: the way the garrison was built facing
    /// </summary>
    [DataField]
    public Angle Facing;

    /// <summary>
    /// Only these, if set (the heavy gun fires only at the big bugs)
    /// </summary>
    [DataField]
    public EntityWhitelist? Targets;

    [DataField]
    public EntityUid? Target;

    [DataField]
    public TimeSpan NextLook;
}

/// <summary>
/// A big bug: worth the heavy gun of a garrison (everything bigger than a larva)
/// </summary>
[RegisterComponent]
public sealed partial class ST14BigBugComponent : Component;

/// <summary>
/// A forward base: troopers of the sector it stands in come back to life in its bunker (ST14SectorSpawnSystem). On the
/// supply crate inside it.
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerBaseComponent : Component;

/// <summary>
/// A radio tower on a bunker: whoever is inside the bunker can zoom out to <see cref="Zoom"/>
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerObservationComponent : Component
{
    [DataField]
    public Vector2 Zoom = new(1.6f, 1.6f);
}

/// <summary>
/// Someone looking out of an observation bunker: their zoom from before, given back when they leave
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerObserverComponent : Component
{
    [DataField]
    public Vector2 MaxZoomBefore = Vector2.One;

    [DataField]
    public EntityUid Bunker;
}

/// <summary>
/// A firing slit in the side of a bunker: nobody gets through, a bullet fired out through it comes up over the ground
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerSlitComponent : Component;

/// <summary>
/// One end of a way between the inside of a bunker and the level above it (the ramp, the ladder): Alt+click goes to
/// <see cref="Other"/> end. A ramp or a ladder puts its ends in place and takes them with it.
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerPassageComponent : Component
{
    [DataField]
    public EntityUid? Other;

    [DataField]
    public LocId Verb = "st14-bunker-passage-go";

    [DataField]
    public float Time = 1f;
}

/// <summary>
/// A ladder in a bunker up through its roof (a modification, not an upgrade): its foot is this, inside; its top
/// (<see cref="TopProto"/>) goes on the roof over it
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerLadderComponent : Component
{
    [DataField]
    public EntProtoId TopProto = "ST14BunkerLadderTop";

    [DataField]
    public EntityUid? Top;
}

/// <summary>
/// A ramp on a bunker: it goes down into it over the side it faces. Its ends are <see cref="OutsideProto"/> on the
/// ground just past that side and <see cref="InsideProto"/> on the floor just inside it.
/// </summary>
[RegisterComponent]
public sealed partial class ST14BunkerRampComponent : Component
{
    [DataField]
    public EntProtoId OutsideProto = "ST14BunkerRampOutside";

    [DataField]
    public EntProtoId InsideProto = "ST14BunkerRampInside";

    [DataField]
    public EntityUid? Outside;

    [DataField]
    public EntityUid? Inside;
}

/// <summary>
/// A modification of a trench tile (barbed wire, a barricade): it stands on the floor of the trench. Built from the
/// surface over the trench, it goes down onto the floor (<c>ST14TrenchModSystem</c>).
/// </summary>
[RegisterComponent]
public sealed partial class ST14TrenchModComponent : Component;
