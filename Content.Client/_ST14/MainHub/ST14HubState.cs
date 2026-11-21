using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Separate client state for the main menu, so lobby music and lobby UI stay away until the player leaves it
/// </summary>
public sealed partial class ST14HubState : State
{
    [Dependency] private IUserInterfaceManager _userInterfaceManager = default!;

    private ST14HubScreen? _screen;

    public ST14HubScreen? Screen => _screen;

    protected override void Startup()
    {
        _screen = new ST14HubScreen();
        _userInterfaceManager.StateRoot.AddChild(_screen);
        LayoutContainer.SetAnchorPreset(_screen, LayoutContainer.LayoutPreset.Wide);
    }

    protected override void Shutdown()
    {
        _screen?.Orphan();
        _screen = null;
    }
}
