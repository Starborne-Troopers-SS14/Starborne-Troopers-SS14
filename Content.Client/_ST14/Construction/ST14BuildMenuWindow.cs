using System.Numerics;
using Content.Shared._ST14.Construction;
using Content.Shared.FixedPoint;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._ST14.Construction;

public sealed class ST14BuildMenuWindow : DefaultWindow
{
    private static readonly Color AvailableCost = Color.FromHex("#59e6ff");
    private static readonly Color UnavailableCost = Color.FromHex("#ff5050");

    public event Action<ST14EngineerBuildablePrototype>? BuildableSelected;

    private readonly BoxContainer _list;
    private readonly List<(ST14BuildMenuEntry Entry, Button Button, Label Cost)> _items = new();

    public ST14BuildMenuWindow()
    {
        Title = Loc.GetString("st14-build-menu-title");
        Resizable = false;

        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };

        Contents.AddChild(_list);
    }

    public void SetEntries(IEntityManager entMan, List<ST14BuildMenuEntry> entries)
    {
        _list.RemoveAllChildren();
        _items.Clear();

        if (entries.Count == 0)
        {
            _list.AddChild(new Label { Text = Loc.GetString("st14-build-menu-empty") });
            return;
        }

        foreach (var entry in entries)
        {
            var cost = new Label
            {
                Text = Loc.GetString("st14-build-menu-cost", ("cost", entry.Buildable.Cost.Int())),
                HorizontalAlignment = HAlignment.Center,
            };

            var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            content.AddChild(new EntityPrototypeView(entry.Buildable.Result, entMan)
            {
                SetSize = new Vector2(48, 48),
                HorizontalAlignment = HAlignment.Center,
            });
            content.AddChild(new Label { Text = entry.Name, HorizontalAlignment = HAlignment.Center });
            content.AddChild(cost);

            var button = new Button { ToggleMode = true, MinWidth = 96 };
            button.AddChild(content);
            button.OnPressed += _ => BuildableSelected?.Invoke(entry.Buildable);

            _list.AddChild(button);
            _items.Add((entry, button, cost));
        }
    }

    public void UpdateState(FixedPoint2? armium, string? selected)
    {
        foreach (var (entry, button, cost) in _items)
        {
            var available = armium >= entry.Buildable.Cost;

            button.Disabled = !available;
            button.Pressed = available && entry.Buildable.ID == selected;
            button.ToolTip = available
                ? null
                : Loc.GetString(armium == null ? "st14-build-menu-no-multitool" : "st14-build-menu-not-enough");

            cost.FontColorOverride = available ? AvailableCost : UnavailableCost;
        }
    }
}
