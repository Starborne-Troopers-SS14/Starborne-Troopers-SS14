using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The window of the battle map: the sector in the middle, the list of the sectors of this battle on the right and, for a
/// member of the command, the tools. The round goes on behind it
/// </summary>
public sealed class ST14BattleMapWindow : DefaultWindow
{
    private static readonly (ST14WarMarkKind Kind, string Key)[] Tools =
    {
        (ST14WarMarkKind.Attack, "attack"),
        (ST14WarMarkKind.Defend, "defend"),
        (ST14WarMarkKind.Rally, "rally"),
        (ST14WarMarkKind.Danger, "danger"),
        (ST14WarMarkKind.Objective, "objective"),
    };

    private readonly ST14BattleMapClientSystem _system;
    private readonly ST14BattleMapView _view;
    private readonly Label _title;
    private readonly Label _note;
    private readonly BoxContainer _sides;
    private readonly BoxContainer _sectors;
    private readonly BoxContainer _tools;
    private EntityUid? _map;

    /// <summary>
    /// The player opened another sector themselves, the map stops following them
    /// </summary>
    private bool _viewPicked;

    public ST14BattleMapWindow(IResourceCache cache, ST14BattleMapClientSystem system)
    {
        _system = system;
        Title = Loc.GetString("st14-battle-map-title");
        SetSize = new Vector2(1100, 780);
        MinSize = new Vector2(760, 560);

        _view = new ST14BattleMapView(cache, system);
        _view.OnCellClicked += cell => Show(cell, true);
        _view.OnMarkPlaced += mark =>
        {
            if (_system.State is { } state)
                _system.AddMark(state.Side, mark);
        };
        _view.OnMarkErased += index =>
        {
            if (_system.State is { } state)
                _system.RemoveMark(state.Side, index);
        };
        _view.OnToolCancelled += () => SetTool(null, false);

        _title = ST14HubStyle.Label(cache, string.Empty, ST14HubFont.SemiBold, 20, ST14HubStyle.GoldLight);
        _note = new Label { Modulate = new Color(0.85f, 0.85f, 0.85f) };
        _sides = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _sectors = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
        _tools = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_sectors);

        var panel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            SetWidth = 250,
            Margin = new Thickness(8, 0, 0, 0),
        };
        panel.AddChild(_title);
        panel.AddChild(_note);
        panel.AddChild(_sides);
        panel.AddChild(new Label { Text = Loc.GetString("st14-battle-map-sectors") });
        panel.AddChild(scroll);
        panel.AddChild(_tools);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        row.AddChild(_view);
        row.AddChild(panel);
        Contents.AddChild(row);

        _system.Changed += Refresh;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _system.Changed -= Refresh;
    }

    protected override void Opened()
    {
        base.Opened();

        _viewPicked = false;
        _map = _system.OwnMap();
        _system.Request();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!IsOpen || _system.OwnMap() == _map)
            return;

        // through a passage: the server tells which sector this is
        _map = _system.OwnMap();
        _system.Request();
    }

    private void SetTool(ST14WarMarkKind? tool, bool eraser)
    {
        _view.Tool = tool;
        _view.Eraser = eraser;
        Refresh();
    }

    private void Show(int cell, bool picked)
    {
        _viewPicked = picked;
        _view.ViewCell = cell;
        Refresh();
    }

    private void Refresh()
    {
        if (_system.State is not { } state)
            return;

        if (!state.Active)
        {
            _view.ViewCell = -1;
            _title.Text = string.Empty;
            _note.Text = Loc.GetString("st14-battle-map-inactive");
            _sides.RemoveAllChildren();
            _sectors.RemoveAllChildren();
            _tools.RemoveAllChildren();
            return;
        }

        var loaded = state.Sectors.Where(s => s.Loaded).OrderBy(s => s.Code).ToList();
        if (!_viewPicked || state.Sectors.All(s => s.Cell != _view.ViewCell))
        {
            var fallback = state.Here >= 0 ? state.Here : state.Target >= 0 ? state.Target : loaded.FirstOrDefault().Cell;
            _view.ViewCell = fallback;
        }

        var shown = state.Sectors.FirstOrDefault(s => s.Cell == _view.ViewCell);
        _title.Text = shown.Code is { } code ? Loc.GetString("st14-battle-map-sector", ("code", code)).ToUpperInvariant() : string.Empty;
        _note.Text = Loc.GetString(shown.Loaded ? "st14-battle-map-open" : "st14-battle-map-closed");

        BuildSides(state);
        BuildSectors(state, loaded);
        BuildTools(state);
    }

    private void BuildSides(ST14BattleMapStateEvent state)
    {
        _sides.RemoveAllChildren();
        if (state.Commands.Length < 2)
            return;

        foreach (var side in state.Commands)
        {
            var button = new Button
            {
                Text = Loc.GetString($"st14-battle-map-side-{(side == ST14GalaxyFaction.Arachnid ? "bugs" : "federation")}"),
                ToggleMode = true,
                Pressed = side == state.Side,
                HorizontalExpand = true,
            };
            button.OnPressed += _ =>
            {
                SetTool(null, false);
                _system.Request(side);
            };
            _sides.AddChild(button);
        }
    }

    private void BuildSectors(ST14BattleMapStateEvent state, List<ST14BattleSector> loaded)
    {
        _sectors.RemoveAllChildren();
        foreach (var sector in loaded)
        {
            var tag = sector.Cell == state.Here ? "st14-battle-map-tag-here"
                : sector.Cell == state.Target ? "st14-battle-map-tag-target"
                : sector.Cell == state.Staging ? "st14-battle-map-tag-staging"
                : null;

            var button = new Button
            {
                Text = tag == null ? sector.Code : $"{sector.Code}  {Loc.GetString(tag)}",
                ToggleMode = true,
                Pressed = sector.Cell == _view.ViewCell,
            };
            button.OnPressed += _ => Show(sector.Cell, true);
            _sectors.AddChild(button);
        }
    }

    private void BuildTools(ST14BattleMapStateEvent state)
    {
        _tools.RemoveAllChildren();
        if (!state.Commands.Contains(state.Side))
        {
            _view.Tool = null;
            _view.Eraser = false;
            return;
        }

        _tools.AddChild(new Label { Text = Loc.GetString("st14-battle-map-plan", ("count", state.Marks.Length), ("max", ST14BattleMapStateEvent.MaxMarks)) });
        foreach (var (kind, key) in Tools)
        {
            var button = new Button { Text = Loc.GetString($"st14-galaxy-tool-{key}"), ToggleMode = true, Pressed = _view.Tool == kind };
            button.OnPressed += _ => SetTool(_view.Tool == kind ? null : kind, false);
            _tools.AddChild(button);
        }

        var eraser = new Button { Text = Loc.GetString("st14-galaxy-tool-eraser"), ToggleMode = true, Pressed = _view.Eraser };
        eraser.OnPressed += _ => SetTool(null, !_view.Eraser);
        _tools.AddChild(eraser);

        var clear = new Button { Text = Loc.GetString("st14-galaxy-tool-clear"), Disabled = state.Marks.Length == 0 };
        clear.OnPressed += _ => _system.RemoveMark(state.Side, -1);
        _tools.AddChild(clear);
    }
}
