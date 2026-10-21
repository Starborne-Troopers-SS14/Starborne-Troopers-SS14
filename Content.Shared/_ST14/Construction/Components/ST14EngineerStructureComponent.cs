using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Construction.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14EngineerStructureComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<ST14EngineerBuildablePrototype>? Buildable;

    [DataField]
    public FixedPoint2 Paid;
}
