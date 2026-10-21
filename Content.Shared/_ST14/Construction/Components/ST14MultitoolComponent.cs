using Content.Shared._ST14.ST14Resources;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Construction.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14MultitoolComponent : Component
{
    public const string CapsuleSlot = "capsule";
    public const string SuitSlot = "jumpsuit";

    [DataField]
    public float Range = 3f;

    [DataField]
    public float BuildSpeed = 1f;

    [DataField]
    public float DismantleSpeed = 1.5f;

    [DataField]
    public float RefundRatio = 1f;

    [DataField]
    public ProtoId<ST14ResourcePrototype> Resource = "Armium";

    [DataField]
    public bool RequiresSuit = true;

    [DataField]
    public SoundSpecifier? BuildSound = new SoundPathSpecifier("/Audio/Effects/singularity.ogg",
        AudioParams.Default.WithVolume(-8f).WithMaxDistance(8f));

    [DataField]
    public SoundSpecifier? DismantleSound = new SoundPathSpecifier("/Audio/Items/drill_use.ogg",
        AudioParams.Default.WithVolume(-4f).WithMaxDistance(8f));

    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField]
    public ST14MultitoolMode Mode;

    [DataField, AutoNetworkedField]
    public EntityUid? User;
}
