using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Input.Binding;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// Opens the battle map with its button on the top bar and its key
/// </summary>
public sealed partial class ST14BattleMapUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IResourceCache _cache = default!;

    private ST14BattleMapWindow? _window;

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

        _window?.Dispose();
        _window = null;
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
        if (_window == null)
        {
            _window = new ST14BattleMapWindow(_cache, EntityManager.System<ST14BattleMapClientSystem>(), EntityManager.System<Robust.Client.GameObjects.SpriteSystem>());
            _window.OnOpen += () => SetPressed(true);
            _window.OnClose += () => SetPressed(false);
        }

        if (_window.IsOpen)
            _window.Close();
        else
            _window.OpenCentered();
    }

    private void SetPressed(bool pressed)
    {
        if (Button is { } button)
            button.Pressed = pressed;
    }
}
