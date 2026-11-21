using System.Linq;
using Content.Client._ST14.Lobby;
using Content.Client.Lobby;
using Content.Shared._ST14.CCVar;
using Robust.Shared.Configuration;
using Content.Client.Lobby.UI;
using Robust.Client;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Sends the player to the hub instead of the lobby until they leave it once per connection
/// </summary>
public sealed partial class ST14HubUIController : UIController, IOnStateEntered<LobbyState>
{
    [Dependency] private IBaseClient _client = default!;
    [Dependency] private IStateManager _stateManager = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private const string MenuButtonName = "ST14HubMenuButton";

    private bool _leftHub;

    public override void Initialize()
    {
        base.Initialize();
        _client.RunLevelChanged += OnRunLevelChanged;
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs args)
    {
        if (args.NewLevel == ClientRunLevel.Initialize)
            _leftHub = false;
    }

    /// <summary>
    /// Called when the server sends the player to the lobby. Returns true if the hub was used instead
    /// </summary>
    public bool TryEnterHub()
    {
        if (_stateManager.CurrentState is ST14HubState)
            return true;

        if (_leftHub || !_cfg.GetCVar(ST14CVars.HubOnConnect))
            return false;

        _stateManager.RequestStateChange<ST14HubState>();
        return true;
    }

    public void ShowHub()
    {
        _stateManager.RequestStateChange<ST14HubState>();
    }

    /// <summary>
    /// Esc in the main menu or the lobby: closes the full screen menu that is open there, one step at a time.
    /// False when none is open, then Esc does what it always did
    /// </summary>
    public bool TryEscape()
    {
        return _stateManager.CurrentState switch
        {
            ST14HubState { Screen: { } hub } => hub.TryEscape(),
            LobbyState { Lobby: { } lobby } => lobby.Children.OfType<ST14LobbyScreen>().FirstOrDefault()?.TryEscape() == true,
            _ => false,
        };
    }

    public LobbyState EnterLobby()
    {
        _leftHub = true;
        return _stateManager.RequestStateChange<LobbyState>();
    }

    public void OnStateEntered(LobbyState state)
    {
        if (state.Lobby is not { } lobby)
            return;

        AddMenuButton(lobby);
        ApplyHubLook(lobby);
    }

    /// <summary>
    /// Lobby in the look of the hub: the animated wallpapers behind it and the panels framed like the hub windows
    /// </summary>
    /// <summary>
    /// Our own lobby screen with animated backgrounds goes over the stock lobby, see <see cref="ST14LobbyScreen"/>
    /// </summary>
    private void ApplyHubLook(LobbyGui lobby)
    {
        ST14LobbyScreen.Apply(lobby, _resCache, UIManager.Stylesheet!);
    }

    private void AddMenuButton(LobbyGui lobby)
    {
        if (lobby.HeaderContainer.Children.Any(c => c.Name == MenuButtonName))
            return;

        var button = new Button
        {
            Name = MenuButtonName,
            Text = Loc.GetString("st14-hub-lobby-menu-button"),
            StyleClasses = { "ButtonBig" },
        };
        button.OnPressed += _ => ShowHub();
        lobby.HeaderContainer.AddChild(button);
    }
}
