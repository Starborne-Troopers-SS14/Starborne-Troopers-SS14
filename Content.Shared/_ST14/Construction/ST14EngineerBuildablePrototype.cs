using Content.Shared.Construction.Conditions;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Construction;

[Prototype("ST14EngineerBuildable")]
public sealed partial class ST14EngineerBuildablePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Result { get; private set; }

    [DataField]
    public EntProtoId Blueprint { get; private set; } = "ST14Blueprint";

    [DataField(required: true)]
    public FixedPoint2 Cost { get; private set; }

    [DataField]
    public float BuildTime { get; private set; } = 10f;

    [DataField]
    public bool CanRotate { get; private set; }

    [DataField]
    public LocId? Name { get; private set; }

    [DataField]
    public List<IConstructionCondition> Conditions { get; private set; } = new();
}
