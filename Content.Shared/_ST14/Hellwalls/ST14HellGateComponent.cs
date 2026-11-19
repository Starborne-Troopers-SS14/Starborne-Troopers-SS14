using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hellwalls;

[RegisterComponent]
public sealed partial class ST14HellGateComponent : Component
{
    [DataField]
    public List<EntProtoId> Parts = new()
    {
        "ST14HellGatePartNear",
        "ST14HellGatePartFar",
    };

    [ViewVariables]
    public List<EntityUid> Spawned = new();
}
