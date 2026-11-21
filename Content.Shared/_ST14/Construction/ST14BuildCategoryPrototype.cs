using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Construction;

/// <summary>
/// A tab of the engineer's build menu: walls, bunkers, trenches, power and so on. Buildables name theirs with
/// <see cref="ST14EngineerBuildablePrototype.Category"/>; the tabs go in <see cref="Order"/>.
/// </summary>
[Prototype("st14BuildCategory")]
public sealed partial class ST14BuildCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField]
    public int Order { get; private set; }
}
