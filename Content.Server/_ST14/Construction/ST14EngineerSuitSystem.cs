using Content.Shared._ST14.Construction.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;

namespace Content.Server._ST14.Construction;

public sealed partial class ST14EngineerSuitSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14EngineerSuitComponent, BeingEquippedAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<ST14EngineerSuitComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<ST14EngineerSuitComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipAttempt(Entity<ST14EngineerSuitComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (ent.Comp.RequiredJob is not { } job || args.Slot != ST14MultitoolComponent.SuitSlot)
            return;

        if (_mind.TryGetMind(args.EquipTarget, out var mindId, out _) && _jobs.MindHasJobWithId(mindId, job))
            return;

        args.Reason = "st14-engineer-suit-wrong-job";
        args.Cancel();
    }

    private void OnEquipped(Entity<ST14EngineerSuitComponent> ent, ref GotEquippedEvent args)
    {
        if (args.Slot != ST14MultitoolComponent.SuitSlot ||
            !TryComp<InventoryComponent>(args.EquipTarget, out var inventory) ||
            !ent.Comp.Templates.TryGetValue(inventory.TemplateId, out var template))
            return;

        ent.Comp.PreviousTemplate = inventory.TemplateId;
        _inventory.SetTemplateId((args.EquipTarget, inventory), template);
    }

    private void OnUnequipped(Entity<ST14EngineerSuitComponent> ent, ref GotUnequippedEvent args)
    {
        if (ent.Comp.PreviousTemplate is not { } previous)
            return;

        ent.Comp.PreviousTemplate = null;

        if (TerminatingOrDeleted(args.EquipTarget) || !TryComp<InventoryComponent>(args.EquipTarget, out var inventory))
            return;

        _inventory.SetTemplateId((args.EquipTarget, inventory), previous);
    }
}
