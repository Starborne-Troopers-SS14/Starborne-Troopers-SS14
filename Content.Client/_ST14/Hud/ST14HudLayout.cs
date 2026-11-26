using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ST14.Hud;

/// <summary>
/// ST14 bottom bar: armour mount, backpack (and the engineer's capsule) left of the hands, belt and both pockets right of them, all one step apart.
/// The bar and the clothing fan over it are centred on the screen; the fan opens on Tab, the stock clothing button is taken off the screen.
/// </summary>
public static class ST14HudLayout
{
    /// <summary>
    /// Height of the hand row over the bottom of the screen: hotbar margin plus one slot
    /// </summary>
    private const float HandsTop = 5 + 64 + 2;

    private static readonly string[] LeftOrder = { "suitstorage", "back", "capsule" };
    private static readonly string[] RightOrder = { "belt", "pocket1", "pocket2" };

    public static void Apply(InventoryGui inventory, HotbarGui hotbar)
    {
        // the controller still drives the hidden button
        inventory.InventoryButton.Orphan();

        KeepOrder(hotbar.SecondHotbar, LeftOrder);
        KeepOrder(hotbar.MainHotbar, RightOrder);

        LayoutContainer.SetAnchorPreset(inventory, LayoutContainer.LayoutPreset.CenterBottom);
        LayoutContainer.SetGrowHorizontal(inventory, LayoutContainer.GrowDirection.Both);
        LayoutContainer.SetGrowVertical(inventory, LayoutContainer.GrowDirection.Begin);
        LayoutContainer.SetMarginBottom(inventory, -HandsTop);
        LayoutContainer.SetMarginTop(inventory, -HandsTop);
        LayoutContainer.SetMarginLeft(inventory, 0);
        LayoutContainer.SetMarginRight(inventory, 0);
    }

    /// <summary>
    /// Slots come in the order of the inventory template, this puts them in the order of the bar every time one is added
    /// </summary>
    private static void KeepOrder(ItemSlotButtonContainer container, string[] order)
    {
        container.OnChildAdded += _ =>
        {
            var sorted = container.Children
                .OrderBy(c => Rank(c, order))
                .ToList();

            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].GetPositionInParent() != i)
                    sorted[i].SetPositionInParent(i);
            }
        };
    }

    private static int Rank(Control control, string[] order)
    {
        var key = control is SlotControl slot ? slot.SlotName : control.Name;
        var index = key == null ? -1 : Array.IndexOf(order, key);
        return index >= 0 ? index : order.Length;
    }
}

