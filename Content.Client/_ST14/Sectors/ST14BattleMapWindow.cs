using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The window of the battle map: the map of the battle in the middle, on the right the sectors of the battle, the squad
/// and, for a member of the command, the tools. The round goes on behind it
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
    private readonly RichTextLabel _note;
    private readonly BoxContainer _sides;
    private readonly BoxContainer _sectors;
    private readonly BoxContainer _squad;
    private readonly BoxContainer _tools;
    private EntityUid? _map;

    // the panels are built again only when what they show changed: the map gets news every second
    private string _sidesKey = string.Empty;
    private string _sectorsKey = string.Empty;
    private string _squadKey = string.Empty;
    private string _toolsKey = string.Empty;

    public ST14BattleMapWindow(IResourceCache cache, ST14BattleMapClientSystem system, SpriteSystem sprite)
    {
        _system = system;
        Title = Loc.GetString("st14-battle-map-title");
        SetSize = new Vector2(1250, 820);
        MinSize = new Vector2(760, 560);

        _view = new ST14BattleMapView(cache, system, sprite);
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

        _note = new RichTextLabel { Modulate = new Color(0.85f, 0.85f, 0.85f) };
        _sides = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        _sectors = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
        _squad = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
        _tools = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
        column.AddChild(_sectors);
        column.AddChild(_squad);
        scroll.AddChild(column);

        var panel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            SetWidth = 260,
            Margin = new Thickness(8, 0, 0, 0),
        };
        panel.AddChild(_note);
        panel.AddChild(_sides);
        panel.AddChild(scroll);
        panel.AddChild(_tools);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        row.AddChild(_view);
        row.AddChild(panel);
        Contents.AddChild(row);

        _system.Changed += Refresh;
        OnClose += _system.Closed;
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

        _map = _system.OwnMap();
        _view.Reframe();
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

    private static Label Heading(string key)
    {
        return new Label { Text = Loc.GetString(key).ToUpperInvariant(), FontColorOverride = ST14HubStyle.GoldLight };
    }

    private void Refresh()
    {
        if (_system.State is not { } state)
            return;

        _note.SetMessage(Loc.GetString(state.Active ? "st14-battle-map-help" : "st14-battle-map-inactive"));
        BuildSides(state);
        BuildSectors(state);
        BuildSquad(state);
        BuildTools(state);
    }

    private void BuildSides(ST14BattleMapStateEvent state)
    {
        var key = $"{state.Active}|{state.Side}|{string.Join(',', state.Commands)}";
        if (key == _sidesKey)
            return;

        _sidesKey = key;
        _sides.RemoveAllChildren();
        if (!state.Active || state.Commands.Length < 2)
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

    private void BuildSectors(ST14BattleMapStateEvent state)
    {
        var loaded = state.Sectors.Where(s => s.Loaded).OrderBy(s => s.Code).ToList();
        var key = $"{state.Active}|{state.Here}|{state.Target}|{state.Staging}|{string.Join(',', loaded.Select(s => s.Cell))}";
        if (key == _sectorsKey)
            return;

        _sectorsKey = key;
        _sectors.RemoveAllChildren();
        if (!state.Active)
            return;

        _sectors.AddChild(Heading("st14-battle-map-sectors"));
        foreach (var sector in loaded)
        {
            var tag = sector.Cell == state.Here ? "st14-battle-map-tag-here"
                : sector.Cell == state.Target ? "st14-battle-map-tag-target"
                : sector.Cell == state.Staging ? "st14-battle-map-tag-staging"
                : null;

            var button = new Button { Text = tag == null ? sector.Code : $"{sector.Code}  {Loc.GetString(tag)}" };
            button.OnPressed += _ => _view.Focus(sector.Cell);
            _sectors.AddChild(button);
        }
    }

    private void BuildSquad(ST14BattleMapStateEvent state)
    {
        var live = _system.Live;
        var key = live == null || !state.Active
            ? "none"
            : $"{live.Squad}|{string.Join(';', live.Squads.Select(s => $"{s.Id}:{s.Leader}:{string.Join(',', s.Members)}"))}";
        if (key == _squadKey)
            return;

        _squadKey = key;
        _squad.RemoveAllChildren();
        if (live == null || !state.Active)
            return;

        _squad.AddChild(Heading("st14-battle-map-squad"));
        if (live.Squads.FirstOrDefault(s => s.Id == live.Squad) is { Members: not null } own)
        {
            _squad.AddChild(new Label { Text = Loc.GetString("st14-battle-map-squad-name", ("number", own.Number)) });
            foreach (var member in own.Members)
            {
                var text = member == own.Leader ? Loc.GetString("st14-battle-map-squad-leader", ("name", member)) : member;
                _squad.AddChild(new Label { Text = "  " + text, FontColorOverride = member == own.Leader ? ST14HubStyle.GoldLight : null });
            }

            var leave = new Button { Text = Loc.GetString("st14-battle-map-squad-leave") };
            leave.OnPressed += _ => _system.LeaveSquad();
            _squad.AddChild(leave);
            return;
        }

        _squad.AddChild(new Label { Text = Loc.GetString("st14-battle-map-squad-none") });
        foreach (var squad in live.Squads.OrderBy(s => s.Number))
        {
            var full = squad.Members.Length >= ST14BattleMapLiveEvent.MaxSquadSize;
            var join = new Button
            {
                Text = Loc.GetString("st14-battle-map-squad-join", ("number", squad.Number), ("leader", squad.Leader),
                    ("count", squad.Members.Length), ("max", ST14BattleMapLiveEvent.MaxSquadSize)),
                Disabled = full,
            };
            join.OnPressed += _ => _system.JoinSquad(squad.Id);
            _squad.AddChild(join);
        }

        var create = new Button { Text = Loc.GetString("st14-battle-map-squad-create") };
        create.OnPressed += _ => _system.CreateSquad();
        _squad.AddChild(create);
    }

    private void BuildTools(ST14BattleMapStateEvent state)
    {
        var can = state.Active && state.Commands.Contains(state.Side);
        var key = $"{can}|{state.Marks.Length}|{_view.Tool}|{_view.Eraser}";
        if (key == _toolsKey)
            return;

        _toolsKey = key;
        _tools.RemoveAllChildren();
        if (!can)
        {
            _view.Tool = null;
            _view.Eraser = false;
            return;
        }

        _tools.AddChild(Heading("st14-battle-map-plan-heading"));
        _tools.AddChild(new Label { Text = Loc.GetString("st14-battle-map-plan", ("count", state.Marks.Length), ("max", ST14BattleMapStateEvent.MaxMarks)) });
        var grid = new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 3 };
        foreach (var (kind, key2) in Tools)
        {
            var button = new Button { Text = Loc.GetString($"st14-galaxy-tool-{key2}"), ToggleMode = true, Pressed = _view.Tool == kind, HorizontalExpand = true };
            button.OnPressed += _ => SetTool(_view.Tool == kind ? null : kind, false);
            grid.AddChild(button);
        }

        var eraser = new Button { Text = Loc.GetString("st14-galaxy-tool-eraser"), ToggleMode = true, Pressed = _view.Eraser, HorizontalExpand = true };
        eraser.OnPressed += _ => SetTool(null, !_view.Eraser);
        grid.AddChild(eraser);
        _tools.AddChild(grid);

        var clear = new Button { Text = Loc.GetString("st14-galaxy-tool-clear"), Disabled = state.Marks.Length == 0 };
        clear.OnPressed += _ => _system.RemoveMark(state.Side, -1);
        _tools.AddChild(clear);
    }
}
