using System.Linq;
using System.Numerics;
using Content.Shared._ST14.Construction;
using Content.Shared.FixedPoint;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.Construction;

/// <summary>
/// The engineer's build menu: a row of tabs (ST14: <see cref="ST14BuildCategoryPrototype"/>, walls, bunkers, trenches
/// and so on) over the buildables of the tab picked. The tab stays picked while the window is reopened.
/// </summary>
public sealed class ST14BuildMenuWindow : DefaultWindow
{
    private static readonly Color AvailableCost = Color.FromHex("#59e6ff");
    private static readonly Color UnavailableCost = Color.FromHex("#ff5050");

    public event Action<ST14EngineerBuildablePrototype>? BuildableSelected;

    private readonly BoxContainer _tabs;
    private readonly BoxContainer _list;
    private readonly List<(ST14BuildMenuEntry Entry, Button Button, Label Cost)> _items = new();
    private readonly List<(ProtoId<ST14BuildCategoryPrototype> Category, Button Button)> _tabButtons = new();

    private IEntityManager? _entMan;
    private List<ST14BuildMenuEntry> _entries = new();
    private ProtoId<ST14BuildCategoryPrototype>? _category;
    private FixedPoint2? _armium;
    private string? _selected;

    public ST14BuildMenuWindow()
    {
        Title = Loc.GetString("st14-build-menu-title");
        Resizable = false;

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        _tabs = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 2,
        };
        _list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
        };

        column.AddChild(_tabs);
        column.AddChild(_list);
        Contents.AddChild(column);
    }

    public void SetEntries(IEntityManager entMan, List<ST14BuildMenuEntry> entries)
    {
        _entMan = entMan;
        _entries = entries;
        _tabs.RemoveAllChildren();
        _tabButtons.Clear();

        if (entries.Count == 0)
        {
            ShowCategory(null);
            return;
        }

        // the tabs that have something in them, in their order
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        var categories = new List<ST14BuildCategoryPrototype>();
        foreach (var entry in entries)
        {
            if (prototypes.TryIndex(entry.Buildable.Category, out var proto) && !categories.Contains(proto))
                categories.Add(proto);
        }

        categories.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var category in categories)
        {
            var id = new ProtoId<ST14BuildCategoryPrototype>(category.ID);
            var tab = new Button
            {
                Text = Loc.GetString(category.Name),
                ToggleMode = true,
                MinWidth = 90,
            };
            tab.OnPressed += _ => ShowCategory(id);
            _tabs.AddChild(tab);
            _tabButtons.Add((id, tab));
        }

        var keep = _category is { } current && _tabButtons.Any(t => t.Category == current);
        ShowCategory(keep ? _category : _tabButtons.Count > 0 ? _tabButtons[0].Category : (ProtoId<ST14BuildCategoryPrototype>?) null);
    }

    private void ShowCategory(ProtoId<ST14BuildCategoryPrototype>? category)
    {
        _category = category;
        _list.RemoveAllChildren();
        _items.Clear();

        foreach (var (id, tab) in _tabButtons)
        {
            tab.Pressed = id == category;
        }

        if (_entries.Count == 0 || _entMan == null)
        {
            _list.AddChild(new Label { Text = Loc.GetString("st14-build-menu-empty") });
            return;
        }

        foreach (var entry in _entries)
        {
            if (entry.Buildable.Category != category)
                continue;

            var cost = new Label
            {
                Text = Loc.GetString("st14-build-menu-cost", ("cost", entry.Buildable.Cost.Int())),
                HorizontalAlignment = HAlignment.Center,
            };

            var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            content.AddChild(new EntityPrototypeView(entry.Buildable.Result, _entMan)
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

        UpdateState(_armium, _selected);
    }

    public void UpdateState(FixedPoint2? armium, string? selected)
    {
        _armium = armium;
        _selected = selected;

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
