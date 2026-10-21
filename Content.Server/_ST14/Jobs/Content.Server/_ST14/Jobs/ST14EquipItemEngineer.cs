using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Jobs;

[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14EquipItemSpecial : JobSpecial
{
    [DataField(required: true)]
    public string Slot = string.Empty;

    [DataField(required: true)]
    public EntProtoId Prototype;

    public override void AfterEquip(EntityUid mob)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var item = entMan.SpawnEntity(Prototype, entMan.GetComponent<TransformComponent>(mob).Coordinates);

        if (!entMan.System<InventorySystem>().TryEquip(mob, item, Slot, silent: true, force: true))
            entMan.System<SharedHandsSystem>().PickupOrDrop(mob, item);
    }
}
