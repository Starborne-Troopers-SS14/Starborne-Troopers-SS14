using Content.Client._ST14.MainHub.Galaxy;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// Opens the map of the war over the screen with its button on the top bar and its key
/// </summary>
public sealed partial class ST14BattleMapUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private ST14BattleMapScreen? _screen;

    private MenuButton? Button => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.BattleMapButton;

    public void OnStateEntered(GameplayState state)
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenBattleMap, InputCmdHandler.FromDelegate(_ => Toggle()))
            .Register<ST14BattleMapUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<ST14BattleMapUIController>();

        _screen?.Orphan();
        _screen?.Dispose();
        _screen = null;
    }

    internal void LoadButton()
    {
        if (Button is { } button)
            button.OnPressed += OnButtonPressed;
    }

    internal void UnloadButton()
    {
        if (Button is { } button)
            button.OnPressed -= OnButtonPressed;
    }

    private void OnButtonPressed(ButtonEventArgs args)
    {
        Toggle();
    }

    public void Toggle()
    {
        if (_screen == null)
        {
            _screen = new ST14BattleMapScreen(EntityManager.System<ST14BattleMapClientSystem>(), EntityManager.System<ST14GalaxyWarClientSystem>(),
                EntityManager.System<SpriteSystem>(), _prototypes);
            _screen.OnBack += Close;
            UIManager.PopupRoot.AddChild(_screen);
            LayoutContainer.SetAnchorPreset(_screen, LayoutContainer.LayoutPreset.Wide);
        }

        if (_screen.Visible)
        {
            Close();
            return;
        }

        // over everything else on the screen, the squad panel in the corner included
        _screen.SetPositionLast();
        _screen.Open();
        SetPressed(true);
    }

    private void Close()
    {
        _screen?.Close();
        SetPressed(false);
    }

    private void SetPressed(bool pressed)
    {
        if (Button is { } button)
            button.Pressed = pressed;
    }
}
