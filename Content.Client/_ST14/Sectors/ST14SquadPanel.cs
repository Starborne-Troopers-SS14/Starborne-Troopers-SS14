using System.Linq;
using Content.Client._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The squad of the player: who is in it and how they are, an invitation to answer, the squads of the side to join.
/// The same panel stands on the right of the screen in a round and in the battle map
/// </summary>
public sealed class ST14SquadPanel : PanelContainer
{
    private static readonly Color Alive = Color.FromHex("#6FE08A");
    private static readonly Color Critical = Color.FromHex("#FFB547");
    private static readonly Color Dead = Color.FromHex("#E0524A");
    private static readonly Color Away = Color.FromHex("#8E897E");

    private readonly IResourceCache _cache;
    private readonly ST14BattleMapClientSystem _system;
    private readonly BoxContainer _column;
    private readonly bool _compact;

    /// <summary>
    /// The short card on the screen asks for the window with the whole of the squads
    /// </summary>
    public event Action? OnOpenWindow;

    public ST14SquadPanel(IResourceCache cache, ST14BattleMapClientSystem system, bool compact)
    {
        _cache = cache;
        _system = system;
        _compact = compact;

        PanelOverride = ST14HubStyle.Flat(Color.FromHex("#0E0D0B").WithAlpha(compact ? 0.86f : 0.6f), ST14HubStyle.Gold.WithAlpha(0.5f), 1);
        _column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(8, 6) };
        AddChild(_column);

        _system.SquadsChanged += Rebuild;
        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _system.SquadsChanged -= Rebuild;
    }

    private Label Text(string text, ST14HubFont font, int size, Color color)
    {
        var label = ST14HubStyle.Label(_cache, text, font, size, color, shadow: true);
        label.ClipText = true;
        return label;
    }

    private Button Plate(string text, Action act)
    {
        var button = new Button { Text = text, HorizontalExpand = true, MinHeight = 28 };
        button.OnPressed += _ => act();
        return button;
    }

    private static Color StatusColor(ST14SquadMemberStatus status)
    {
        return status switch
        {
            ST14SquadMemberStatus.Alive => Alive,
            ST14SquadMemberStatus.Critical => Critical,
            ST14SquadMemberStatus.Dead => Dead,
            _ => Away,
        };
    }

    private void Rebuild()
    {
        _column.RemoveAllChildren();
        var state = _system.Squads;
        Visible = state is { Active: true };
        if (state is not { Active: true })
            return;

        if (state.Invite is { } invite)
        {
            _column.AddChild(Text(Loc.GetString("st14-squad-invite", ("from", invite.From), ("number", invite.Number)).ToUpperInvariant(),
                ST14HubFont.SemiBold, 13, ST14HubStyle.GoldLight));
            var answers = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            answers.AddChild(Plate(Loc.GetString("st14-squad-accept"), () => _system.AnswerInvite(true)));
            answers.AddChild(Plate(Loc.GetString("st14-squad-decline"), () => _system.AnswerInvite(false)));
            _column.AddChild(answers);
        }

        // on the screen only an invitation to answer, nothing else: the squads live in their window
        if (_compact)
        {
            Visible = state.Invite != null;
            return;
        }

        if (state.Squads.FirstOrDefault(s => s.Id == state.Squad) is { Members: not null } own)
        {
            _column.AddChild(Text(Loc.GetString("st14-squad-title", ("number", own.Number)).ToUpperInvariant(), ST14HubFont.Bold, 16, ST14HubStyle.GoldLight));
            foreach (var member in own.Members)
            {
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
                row.AddChild(Text("●", ST14HubFont.Bold, 13, StatusColor(member.Status)));
                var name = member.Leader ? Loc.GetString("st14-squad-leader", ("name", member.Name)) : member.Name;
                var label = Text(name, ST14HubFont.Medium, 13, member.Leader ? ST14HubStyle.GoldLight : Color.White);
                label.HorizontalExpand = true;
                row.AddChild(label);
                _column.AddChild(row);
            }

            // the card on the screen only shows who is who; leaving and inviting are in the window
            if (_compact)
            {
                _column.AddChild(Text(OpenHint(), ST14HubFont.Regular, 12, ST14HqArtMuted));
                return;
            }

            _column.AddChild(Text(Loc.GetString("st14-squad-how-to-invite"), ST14HubFont.Regular, 12, ST14HqArtMuted));
            _column.AddChild(Plate(Loc.GetString("st14-squad-leave"), _system.LeaveSquad));
            return;
        }

        // not in a squad: the card is one button to the window, it takes no room in a fight
        if (_compact)
        {
            _column.AddChild(Plate($"{Loc.GetString("st14-squad-none")}  ·  {OpenHint()}".ToUpperInvariant(), () => OnOpenWindow?.Invoke()));
            return;
        }

        _column.AddChild(Text(Loc.GetString("st14-squad-none").ToUpperInvariant(), ST14HubFont.Bold, 14, ST14HubStyle.GoldLight));

        foreach (var squad in state.Squads)
        {
            var leader = squad.Members.FirstOrDefault(m => m.Leader).Name ?? string.Empty;
            var join = Plate(Loc.GetString("st14-squad-join", ("number", squad.Number), ("leader", leader),
                ("count", squad.Members.Length), ("max", ST14SquadStateEvent.MaxSquadSize)), () => _system.JoinSquad(squad.Id));
            join.Disabled = squad.Members.Length >= ST14SquadStateEvent.MaxSquadSize;
            _column.AddChild(join);
        }

        // ST14: of the troopers only a squad leader forms a squad
        if (state.CanCreate)
            _column.AddChild(Plate(Loc.GetString("st14-squad-create"), _system.CreateSquad));
        else
            _column.AddChild(Text(Loc.GetString("st14-squad-create-leader-only"), ST14HubFont.Regular, 12, ST14HqArtMuted));
    }

    private static readonly Color ST14HqArtMuted = new(0.68f, 0.71f, 0.75f);

    private static string OpenHint()
    {
        var input = IoCManager.Resolve<IInputManager>();
        var key = input.TryGetKeyBinding(ContentKeyFunctions.OpenSquads, out var binding) ? binding.GetKeyString() : "J";
        return Loc.GetString("st14-squad-open-hint", ("key", key));
    }
}
