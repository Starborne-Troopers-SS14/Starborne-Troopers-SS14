using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Medicine;

public enum ST14ProsthesisLimb : byte
{
    Arm,
    Leg,
}

/// <summary>
/// A prosthesis ready to be fitted to a stump: an arm with its hand, or a leg with its foot. A field one goes on
/// anywhere and fast, but a leg of it limps; a steel one works like the real limb, and only goes on a patient lying down
/// </summary>
[RegisterComponent]
public sealed partial class ST14ProsthesisItemComponent : Component
{
    [DataField(required: true)]
    public ST14ProsthesisLimb Limb;

    [DataField(required: true)]
    public EntProtoId LeftLimb;

    [DataField(required: true)]
    public EntProtoId LeftEnd;

    [DataField(required: true)]
    public EntProtoId RightLimb;

    [DataField(required: true)]
    public EntProtoId RightEnd;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The patient has to lie down, on a bed or the ground, for it to be fitted
    /// </summary>
    [DataField]
    public bool NeedsLying;

    [DataField]
    public SoundSpecifier? FitSound;
}

/// <summary>
/// An arm, hand, leg or foot that is not flesh. A field leg limps
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ST14ProsthesisComponent : Component
{
    [DataField]
    public bool Field;
}

[Serializable, NetSerializable]
public sealed partial class ST14FitProsthesisDoAfterEvent : SimpleDoAfterEvent;
