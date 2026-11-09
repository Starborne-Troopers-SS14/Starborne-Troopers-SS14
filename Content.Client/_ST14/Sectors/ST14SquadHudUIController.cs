using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The squads of a sector round. The whole of it, the squads to join, to make and to leave, lives in its own window
/// on the top bar and its key; on the right of the screen, under the alerts, only a short card: the members of the
/// own squad, an invitation to answer, or the key that opens the window
/// </summary>
public sealed partial class ST14SquadHudUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private const float Width = 240;
    private const float Gap = 10;

    [Dependency] private IResourceCache _cache = default!;

    private ST14SquadPanel? _panel;
    private DefaultWindow? _window;

    private MenuButton? Button => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.SquadsButton;

    public void OnStateEntered(GameplayState state)
    {
        var system = EntityManager.System<ST14BattleMapClientSystem>();
        _panel = new ST14SquadPanel(_cache, system, true) { SetWidth = Width };
        _panel.OnOpenWindow += Toggle;
        UIManager.PopupRoot.AddChild(_panel);
        system.RequestSquads();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenSquads, InputCmdHandler.FromDelegate(_ => Toggle()))
            .Register<ST14SquadHudUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        CommandBinds.Unregister<ST14SquadHudUIController>();

        _panel?.Orphan();
        _panel?.Dispose();
        _panel = null;

        _window?.Close();
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
        if (_window is { IsOpen: true })
        {
            _window.Close();
            return;
        }

        var system = EntityManager.System<ST14BattleMapClientSystem>();
        if (_window == null)
        {
            _window = new DefaultWindow
            {
                Title = Loc.GetString("st14-squad-window-title"),
                MinSize = new Vector2(380, 420),
            };
            _window.Contents.AddChild(new ST14SquadPanel(_cache, system, false) { VerticalExpand = true });
            _window.OnClose += () => SetPressed(false);
            _window.OnOpen += () => SetPressed(true);
        }

        system.RequestSquads();
        _window.OpenCentered();
    }

    private void SetPressed(bool pressed)
    {
        if (Button is { } button)
            button.Pressed = pressed;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_panel == null)
            return;

        // under the column of alerts, which grows and shrinks
        var root = UIManager.PopupRoot;
        var top = 300f;
        if (UIManager.GetActiveUIWidgetOrNull<AlertsUI>() is { } alerts)
            top = (alerts.GlobalPosition.Y + alerts.Size.Y) + Gap;

        LayoutContainer.SetPosition(_panel, new Vector2(root.Size.X - Width - Gap, top));
    }
}
