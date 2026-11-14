using Content.Client.GameTicking;
using Content.Client._ST14.RoundEnd; // ST14
using Robust.Client.ResourceManagement; // ST14
using Robust.Client.UserInterface.Controls; // ST14
using Content.Shared.GameTicking;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Client.RoundEnd;

[UsedImplicitly]
public sealed partial class RoundEndSummaryUIController : UIController,
    IOnSystemLoaded<ClientGameTicker>
{
    [Dependency] private IInputManager _input = default!;

    private RoundEndSummaryWindow? _window;
    [Dependency] private IResourceCache _cache = default!; // ST14
    private ST14RoundCreditsScreen? _credits; // ST14

    private void ToggleScoreboardWindow(ICommonSession? session = null)
    {
        if (_window == null)
            return;

        if (_window.IsOpen)
        {
            _window.Close();
        }
        else
        {
            _window.OpenCenteredRight();
            _window.MoveToFront();
        }
    }

    public void OpenRoundEndSummaryWindow(RoundEndMessageEvent message)
    {
        // Don't open duplicate windows (mainly for replays).
        if (_window?.RoundId == message.RoundId)
            return;

        _window = new RoundEndSummaryWindow(message.GamemodeTitle, message.RoundEndText,
            message.RoundDuration, message.RoundId, message.AllPlayersEndInfo);

        // ST14-START: the round ends with the credits, the summary stays behind its key
        _window.Close();
        _credits?.Close();
        _credits = new ST14RoundCreditsScreen(_cache, message);
        _credits.OnClosed += () => _credits = null;
        UIManager.PopupRoot.AddChild(_credits);
        LayoutContainer.SetAnchorPreset(_credits, LayoutContainer.LayoutPreset.Wide);
        _credits.SetPositionLast();
        // ST14-STOP
    }

    public void OnSystemLoaded(ClientGameTicker system)
    {
        _input.SetInputCommand(ContentKeyFunctions.ToggleRoundEndSummaryWindow,
            InputCmdHandler.FromDelegate(ToggleScoreboardWindow));
    }
}
