using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._ST14.Hive;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.Hive;

/// <summary>
/// The evolution menu of a bug. Tabs T1 T2 T3 | hive upgrades | T4 T5 | X; inside a caste tab the main line
/// of the tab is in the centre and side lines to the left and right
/// </summary>
public sealed partial class ST14HiveEvolutionWindow : DefaultWindow
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private static readonly Color Accent = Color.FromHex("#FF9A52");
    private static readonly Color CardBackground = Color.FromHex("#1E1611");
    private static readonly Color CardBorder = Color.FromHex("#5A3A22");
    private static readonly Color CurrentBorder = Color.FromHex("#FFD27A");

    public event Action<ProtoId<ST14HiveCastePrototype>>? OnEvolve;
    public event Action<ST14HiveTab>? OnOpenTab;
    public event Action<ProtoId<ST14HiveCastePrototype>>? OnOpenSpecies;

    private readonly Label _pointsLabel;
    private readonly BoxContainer _tabBar;
    private readonly BoxContainer _body;

    /// <summary>
    /// The selected page: a caste tab, or null for the hive upgrades
    /// </summary>
    private ST14HiveTab? _page = ST14HiveTab.T1;

    private ST14HiveEvolutionState? _state;

    public ST14HiveEvolutionWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = Loc.GetString("st14-hive-ui-title");
        MinSize = new Vector2(760, 520);
        SetSize = new Vector2(820, 600);

        _pointsLabel = new Label { HorizontalExpand = true };
        _tabBar = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 4,
        };
        _body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        root.AddChild(_pointsLabel);
        root.AddChild(_tabBar);
        root.AddChild(new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            Children = { _body },
        });

        Contents.AddChild(root);
    }

    public void UpdateState(ST14HiveEvolutionState state)
    {
        _state = state;
        if (!state.IsBrain && _page == null)
            _page = ST14HiveTab.T1;

        Rebuild();
    }

    private void Rebuild()
    {
        if (_state is not { } state)
            return;

        var points = Loc.GetString("st14-hive-ui-points",
            ("evolution", state.EvolutionPoints),
            ("hive", state.HivePoints));

        if (state.LarvaBiomass is { } biomass)
            points += "   " + Loc.GetString("st14-hive-ui-biomass", ("biomass", biomass), ("needed", state.LarvaBiomassNeeded));

        _pointsLabel.Text = points;

        BuildTabBar(state);

        _body.RemoveAllChildren();
        if (_page is { } tab)
            BuildCasteTab(state, tab);
        else
            BuildHivePage(state);
    }

    private void BuildTabBar(ST14HiveEvolutionState state)
    {
        _tabBar.RemoveAllChildren();

        AddTabButton(state, ST14HiveTab.T1);
        AddTabButton(state, ST14HiveTab.T2);
        AddTabButton(state, ST14HiveTab.T3);

        // only the brain bug decides where the hive evolves, the others do not even see the upgrades
        if (state.IsBrain)
        {
            var hive = new Button
            {
                Text = Loc.GetString("st14-hive-ui-tab-hive"),
                ToggleMode = true,
                Pressed = _page == null,
                MinWidth = 140,
            };
            hive.OnPressed += _ =>
            {
                _page = null;
                Rebuild();
            };
            _tabBar.AddChild(hive);
        }

        AddTabButton(state, ST14HiveTab.T4);
        AddTabButton(state, ST14HiveTab.T5);
        AddTabButton(state, ST14HiveTab.X);
    }

    private void AddTabButton(ST14HiveEvolutionState state, ST14HiveTab tab)
    {
        var open = state.OpenTabs.Contains(tab);
        var button = new Button
        {
            Text = TabName(tab),
            ToggleMode = true,
            Pressed = _page == tab,
            MinWidth = 56,
            ToolTip = open ? null : Loc.GetString("st14-hive-ui-tab-closed-tooltip"),
        };

        if (!open)
            button.ModulateSelfOverride = Color.FromHex("#8A8A8A");

        button.OnPressed += _ =>
        {
            _page = tab;
            Rebuild();
        };
        _tabBar.AddChild(button);
    }

    private bool HasCastes(ST14HiveEvolutionState state, ST14HiveTab tab)
    {
        return state.Castes.Any(entry => _prototype.Index(entry.Caste).Tab == tab);
    }

    private static string TabName(ST14HiveTab tab)
    {
        return tab == ST14HiveTab.X ? "X" : tab.ToString();
    }

    #region Castes

    private void BuildCasteTab(ST14HiveEvolutionState state, ST14HiveTab tab)
    {
        if (!HasCastes(state, tab))
        {
            _body.AddChild(new Label
            {
                Text = Loc.GetString("st14-hive-ui-tab-empty"),
                StyleClasses = { StyleClass.LabelWeak },
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 4),
            });
            return;
        }

        if (!state.OpenTabs.Contains(tab))
        {
            var cost = _prototype.Index(ST14HiveSettingsPrototype.Default).TabCosts.GetValueOrDefault(tab);
            _body.AddChild(new Label
            {
                Text = Loc.GetString("st14-hive-ui-tab-closed", ("cost", cost)),
                FontColorOverride = Accent,
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 4),
            });
        }

        var columns = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };

        foreach (var column in new[] { ST14HiveColumn.Left, ST14HiveColumn.Center, ST14HiveColumn.Right })
        {
            var box = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                SeparationOverride = 6,
            };

            var castes = state.Castes
                .Select(entry => (entry, proto: _prototype.Index(entry.Caste)))
                .Where(c => c.proto.Tab == tab && c.proto.Column == column)
                .OrderBy(c => c.proto.Order)
                .ThenBy(c => c.proto.Tier);

            foreach (var (entry, proto) in castes)
            {
                box.AddChild(MakeCasteCard(state, entry, proto));
            }

            columns.AddChild(box);
        }

        _body.AddChild(columns);
    }

    private Control MakeCasteCard(ST14HiveEvolutionState state, ST14CasteEntry entry, ST14HiveCastePrototype caste)
    {
        var current = entry.Status == ST14CasteStatus.Current;
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = CardBackground,
                BorderColor = current ? CurrentBorder : CardBorder,
                BorderThickness = new Thickness(current ? 2 : 1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 6,
            },
            HorizontalExpand = true,
        };

        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var view = new EntityPrototypeView
        {
            SetSize = new Vector2(64, 64),
            Stretch = SpriteView.StretchMode.Fit,
        };
        view.SetPrototype(caste.Entity);
        header.AddChild(view);

        var titles = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalAlignment = VAlignment.Center };
        titles.AddChild(new Label
        {
            Text = Loc.GetString(caste.Name),
            FontColorOverride = Accent,
            StyleClasses = { StyleClass.LabelHeading },
        });
        titles.AddChild(new Label
        {
            Text = Loc.GetString("st14-hive-ui-tier", ("tier", caste.Tier), ("cost", caste.Cost)),
            StyleClasses = { StyleClass.LabelWeak },
        });
        header.AddChild(titles);
        box.AddChild(header);

        if (caste.Description != null)
        {
            box.AddChild(new Label
            {
                Text = Loc.GetString(caste.Description.Value),
                ClipText = false,
                Margin = new Thickness(0, 2),
            });
        }

        if (caste.EvolvesFrom.Count > 0)
        {
            var from = string.Join(", ", caste.EvolvesFrom.Select(id => Loc.GetString(_prototype.Index(id).Name)));
            box.AddChild(new Label
            {
                Text = Loc.GetString("st14-hive-ui-from", ("castes", from)),
                StyleClasses = { StyleClass.LabelWeak },
            });
        }

        if (state.IsLarva ? caste.FromLarva : !current)
        {
            var button = new Button
            {
                Text = Loc.GetString(StatusText(entry.Status, state.IsLarva)),
                Disabled = entry.Status != ST14CasteStatus.Available,
                Margin = new Thickness(0, 4, 0, 0),
            };
            button.OnPressed += _ => OnEvolve?.Invoke(entry.Caste);
            box.AddChild(button);
        }
        else if (current)
        {
            box.AddChild(new Label { Text = Loc.GetString("st14-hive-ui-status-current"), FontColorOverride = CurrentBorder });
        }

        panel.AddChild(box);
        return panel;
    }

    private static string StatusText(ST14CasteStatus status, bool larva)
    {
        return status switch
        {
            ST14CasteStatus.Available => larva ? "st14-hive-ui-hatch" : "st14-hive-ui-evolve",
            ST14CasteStatus.NotEnoughPoints => "st14-hive-ui-status-points",
            ST14CasteStatus.NotEnoughBiomass => "st14-hive-ui-status-biomass",
            ST14CasteStatus.TabClosed => "st14-hive-ui-status-tab",
            ST14CasteStatus.SpeciesClosed => "st14-hive-ui-status-species",
            ST14CasteStatus.Current => "st14-hive-ui-status-current",
            _ => "st14-hive-ui-status-unreachable",
        };
    }

    #endregion

    #region Hive

    private void BuildHivePage(ST14HiveEvolutionState state)
    {
        _body.AddChild(new Label
        {
            Text = Loc.GetString("st14-hive-ui-hive-points", ("points", state.HivePoints)),
            FontColorOverride = Accent,
            StyleClasses = { StyleClass.LabelHeading },
        });

        _body.AddChild(new Label
        {
            Text = Loc.GetString(state.IsBrain ? "st14-hive-ui-hive-brain" : "st14-hive-ui-hive-not-brain"),
            StyleClasses = { StyleClass.LabelWeak },
            Margin = new Thickness(0, 0, 0, 6),
        });

        _body.AddChild(new Label { Text = Loc.GetString("st14-hive-ui-hive-tabs"), StyleClasses = { StyleClass.LabelSubHeading } });

        var settings = _prototype.Index(ST14HiveSettingsPrototype.Default);
        foreach (var (tab, cost) in settings.TabCosts.OrderBy(t => t.Key))
        {
            var open = state.OpenTabs.Contains(tab);
            var hasCastes = HasCastes(state, tab);
            var name = Loc.GetString("st14-hive-ui-hive-tab", ("tab", TabName(tab)));
            if (!hasCastes)
                name += " " + Loc.GetString("st14-hive-ui-hive-tab-empty");

            _body.AddChild(MakeUpgradeRow(
                name,
                cost,
                open,
                state.IsBrain && hasCastes && state.HivePoints >= cost,
                () => OnOpenTab?.Invoke(tab)));
        }

        _body.AddChild(new Label
        {
            Text = Loc.GetString("st14-hive-ui-hive-species"),
            StyleClasses = { StyleClass.LabelSubHeading },
            Margin = new Thickness(0, 8, 0, 0),
        });

        var species = state.Castes
            .Select(entry => _prototype.Index(entry.Caste))
            .Where(c => c.HiveCost > 0)
            .OrderBy(c => c.Tab)
            .ThenBy(c => c.Tier);

        foreach (var caste in species)
        {
            var tabOpen = state.OpenTabs.Contains(caste.Tab);
            var open = state.OpenSpecies.Contains(caste.ID);
            _body.AddChild(MakeUpgradeRow(
                $"{TabName(caste.Tab)}: {Loc.GetString(caste.Name)}",
                caste.HiveCost,
                open,
                state.IsBrain && tabOpen && state.HivePoints >= caste.HiveCost,
                () => OnOpenSpecies?.Invoke(caste.ID)));
        }
    }

    private Control MakeUpgradeRow(string name, int cost, bool open, bool canBuy, Action onBuy)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(8, 1),
        };

        row.AddChild(new Label { Text = name, HorizontalExpand = true });

        if (open)
        {
            row.AddChild(new Label { Text = Loc.GetString("st14-hive-ui-hive-open"), FontColorOverride = CurrentBorder });
            return row;
        }

        var button = new Button
        {
            Text = Loc.GetString("st14-hive-ui-hive-buy", ("cost", cost)),
            Disabled = !canBuy,
            MinWidth = 160,
        };
        button.OnPressed += _ => onBuy();
        row.AddChild(button);
        return row;
    }

    #endregion
}
