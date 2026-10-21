using Content.Shared.Inventory;
using Content.Shared.Roles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Construction.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ST14EngineerSuitComponent : Component
{
    [DataField]
    public Dictionary<ProtoId<InventoryTemplatePrototype>, ProtoId<InventoryTemplatePrototype>> Templates = new()
    {
        { "human", "ST14HumanEngineer" },
    };

    [DataField]
    public ProtoId<JobPrototype>? RequiredJob;

    [ViewVariables]
    public ProtoId<InventoryTemplatePrototype>? PreviousTemplate;
}
