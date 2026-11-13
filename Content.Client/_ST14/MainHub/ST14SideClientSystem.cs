using Content.Shared._ST14.MainHub;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// The side the player is bound to in the war over the current region, as the server told it
/// </summary>
public sealed class ST14SideClientSystem : EntitySystem
{
    public ST14GalaxyFaction? Side { get; private set; }

    /// <summary>
    /// The code of the region fought for, empty between regions
    /// </summary>
    public string Region { get; private set; } = string.Empty;

    /// <summary>
    /// The server answered at least once: until then a missing side means nothing
    /// </summary>
    public bool Known { get; private set; }

    /// <summary>
    /// The player picked the side on the choice screen of this client, not the server binding them by their first job
    /// </summary>
    public bool ChosenHere { get; private set; }

    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ST14SideStateEvent>(OnState);
    }

    private void OnState(ST14SideStateEvent ev)
    {
        Side = ev.Side;
        Region = ev.Region;
        Known = true;
        Changed?.Invoke();
    }

    public void Request()
    {
        RaiseNetworkEvent(new ST14SideRequestEvent());
    }

    public void Choose(ST14GalaxyFaction side)
    {
        ChosenHere = true;
        RaiseNetworkEvent(new ST14SideChooseEvent(side));
    }
}
