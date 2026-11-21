using Content.Client.UserInterface.Systems.Info;
using Content.Shared.Input;
using JetBrains.Annotations;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;

namespace Content.Client.UserInterface.Systems.EscapeMenu;

[UsedImplicitly]
public sealed partial class EscapeContextUIController : UIController
{
    [Dependency] private IInputManager _inputManager = default!;

    [Dependency] private CloseRecentWindowUIController _closeRecentWindowUIController = default!;
    [Dependency] private EscapeUIController _escapeUIController = default!;

    public override void Initialize()
    {
        _inputManager.SetInputCommand(ContentKeyFunctions.EscapeContext,
            InputCmdHandler.FromDelegate(_ => CloseWindowOrOpenGameMenu()));
    }

    private void CloseWindowOrOpenGameMenu()
    {
        // ST14: the map of the war over the whole screen closes first
        if (UIManager.GetUIController<Content.Client._ST14.Sectors.ST14BattleMapUIController>().CloseIfOpen())
            return;

        if (_closeRecentWindowUIController.HasClosableWindow())
        {
            _closeRecentWindowUIController.CloseMostRecentWindow();
        }
        // ST14: then the full screen menus of the main menu and the lobby, a step back at a time
        else if (UIManager.GetUIController<Content.Client._ST14.MainHub.ST14HubUIController>().TryEscape())
        {
        }
        else
        {
            _escapeUIController.ToggleWindow();
        }
    }
}
