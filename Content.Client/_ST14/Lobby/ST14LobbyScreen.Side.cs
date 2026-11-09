using Content.Client._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Lobby;

/// <summary>
/// The side of the player for the war over the region: chosen once here, kept until the region is taken whole.
/// A choice needs a second press, it cannot be undone
/// </summary>
public sealed partial class ST14LobbyScreen
{
    private RichTextLabel? _sideText;
    private BoxContainer? _sideButtons;
    private Button? _pickFederation;
    private Button? _pickArachnids;
    private ST14GalaxyFaction? _confirming;
    private ST14SideChoiceScreen? _sideChoice;

    private Control BuildSide()
    {
        var sides = _entMan.System<ST14SideClientSystem>();
        var frame = new ST14LobbyFrame(_cache, Loc.GetString("st14-side-title").ToUpperInvariant());

        _sideText = new RichTextLabel { HorizontalExpand = true };
        frame.Body.AddChild(_sideText);

        _sideButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        _pickFederation = SideButton(ST14GalaxyFaction.Federation);
        _pickArachnids = SideButton(ST14GalaxyFaction.Arachnid);
        _sideButtons.AddChild(_pickFederation);
        _sideButtons.AddChild(_pickArachnids);
        frame.Body.AddChild(_sideButtons);

        // with no side yet the choice covers the whole lobby, see Build
        _sideChoice = new ST14SideChoiceScreen(_cache, sides);

        sides.Changed += UpdateSide;
        sides.Request();
        UpdateSide();
        return frame;
    }

    private Button SideButton(ST14GalaxyFaction side)
    {
        var button = new Button { HorizontalExpand = true, MinHeight = 36 };
        button.Label.FontOverride = ST14HubStyle.Font(_cache, ST14HubFont.SemiBold, 14);
        button.OnPressed += _ =>
        {
            if (_confirming != side)
            {
                _confirming = side;
                UpdateSide();
                return;
            }

            _confirming = null;
            _entMan.System<ST14SideClientSystem>().Choose(side);
        };

        return button;
    }

    private void UpdateSide()
    {
        if (_sideText == null || _sideButtons == null || _pickFederation == null || _pickArachnids == null)
            return;

        var sides = _entMan.System<ST14SideClientSystem>();
        _sideChoice?.Update();
        if (sides.Side is { } side)
        {
            var name = Loc.GetString(side == ST14GalaxyFaction.Arachnid ? "st14-side-arachnids" : "st14-side-federation");
            Say(sides.Region.Length > 0
                ? Loc.GetString("st14-side-chosen-region", ("side", name), ("region", sides.Region))
                : Loc.GetString("st14-side-chosen", ("side", name)));
            _sideButtons.Visible = false;
            _confirming = null;
        }
        else
        {
            Say(Loc.GetString("st14-side-none"));
            _sideButtons.Visible = sides.Known;
            _pickFederation.Text = Pick(ST14GalaxyFaction.Federation, "st14-side-pick-federation");
            _pickArachnids.Text = Pick(ST14GalaxyFaction.Arachnid, "st14-side-pick-arachnids");
        }
    }

    private void Say(string text)
    {
        var message = new FormattedMessage();
        message.PushColor(ST14LobbyStyle.Text);
        message.AddText(text);
        message.Pop();
        _sideText?.SetMessage(message);
    }

    private string Pick(ST14GalaxyFaction side, string key)
    {
        var text = Loc.GetString(key);
        return _confirming == side ? Loc.GetString("st14-side-confirm", ("side", text)) : text;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _entMan.System<ST14SideClientSystem>().Changed -= UpdateSide;
    }
}
