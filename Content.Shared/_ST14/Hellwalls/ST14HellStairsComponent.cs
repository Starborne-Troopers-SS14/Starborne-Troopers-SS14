using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hellwalls;

[RegisterComponent]
public sealed partial class ST14HellStairsComponent : Component
{
    [DataField]
    public EntProtoId Flight = "ST14HellStairsFlight";

    [DataField]
    public EntProtoId Step = "ST14HellStairsStep";

    [DataField]
    public int Steps = 3;

    [DataField]
    public float Height = 1.9f;

    [ViewVariables]
    public List<EntityUid> Spawned = new();
}
