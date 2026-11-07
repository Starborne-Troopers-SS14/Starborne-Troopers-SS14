using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// Puts the squad panel on the right of the screen, under the alerts, while a sector round is fought
/// </summary>
public sealed partial class ST14SquadHudUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    private const float Width = 240;
    private const float Gap = 10;

    [Dependency] private IResourceCache _cache = default!;

    private ST14SquadPanel? _panel;

    public void OnStateEntered(GameplayState state)
    {
        var system = EntityManager.System<ST14BattleMapClientSystem>();
        _panel = new ST14SquadPanel(_cache, system, true) { SetWidth = Width };
        UIManager.PopupRoot.AddChild(_panel);
        system.RequestSquads();
    }

    public void OnStateExited(GameplayState state)
    {
        _panel?.Orphan();
        _panel?.Dispose();
        _panel = null;
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
