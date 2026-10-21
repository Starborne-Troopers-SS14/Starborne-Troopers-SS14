using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._ST14.Construction.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ST14BlueprintComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<ST14EngineerBuildablePrototype>? Buildable;

    [DataField]
    public SoundSpecifier? CompleteSound = new SoundPathSpecifier("/Audio/Items/deconstruct.ogg");

    [DataField]
    public FixedPoint2 Paid;

    [DataField, AutoNetworkedField]
    public float Progress;

    [DataField, AutoNetworkedField]
    public float Speed;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan LastUpdate;
}
