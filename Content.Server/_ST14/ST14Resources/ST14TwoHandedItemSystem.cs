using Content.Shared._ST14.ST14Resources.Components;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Popups;

namespace Content.Server._ST14.ST14Resources;

/// <summary>
/// Двуручные предметы. Работает только на сервере: виртуальный предмет во второй руке
/// создаёт сервер, клиент просто получает его по сети и ничего не предсказывает.
/// </summary>
public sealed partial class ST14TwoHandedItemSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedVirtualItemSystem _virtualItem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14TwoHandedItemComponent, GettingPickedUpAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<ST14TwoHandedItemComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<ST14TwoHandedItemComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<ST14TwoHandedItemComponent, VirtualItemDeletedEvent>(OnVirtualItemDeleted);
    }

    private void OnPickupAttempt(Entity<ST14TwoHandedItemComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (args.Cancelled || _hands.CountFreeHands(args.User) >= ent.Comp.HandsNeeded)
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("st14-two-handed-need-hands"), args.User, args.User);
    }

    private void OnEquipped(Entity<ST14TwoHandedItemComponent> ent, ref GotEquippedHandEvent args)
    {
        for (var i = 0; i < ent.Comp.HandsNeeded - 1; i++)
        {
            _virtualItem.TrySpawnVirtualItemInHand(ent, args.User, true);
        }
    }

    private void OnUnequipped(Entity<ST14TwoHandedItemComponent> ent, ref GotUnequippedHandEvent args)
    {
        _virtualItem.DeleteInHandsMatching(args.User, ent);
    }

    private void OnVirtualItemDeleted(Entity<ST14TwoHandedItemComponent> ent, ref VirtualItemDeletedEvent args)
    {
        if (args.BlockingEntity != ent.Owner)
            return;

        _hands.TryDrop(args.User, ent.Owner);
    }
}
