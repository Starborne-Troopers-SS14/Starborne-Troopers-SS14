using Content.Shared._ST14.MainHub;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// Keeps the last state of the galactic war the server sent, and sends the orders of the High Command
/// </summary>
public sealed class ST14GalaxyWarClientSystem : EntitySystem
{
    private readonly Dictionary<string, ST14GalaxyWarStateEvent> _states = new();

    public event Action<ST14GalaxyWarStateEvent>? StateReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ST14GalaxyWarStateEvent>(OnState);
    }

    public bool TryGetState(string planet, out ST14GalaxyWarStateEvent state)
    {
        return _states.TryGetValue(planet, out state!);
    }

    public void Request()
    {
        RaiseNetworkEvent(new ST14GalaxyWarRequestEvent());
    }

    public void Order(string planet, int cell)
    {
        RaiseNetworkEvent(new ST14GalaxyWarOrderEvent(planet, cell));
    }

    private void OnState(ST14GalaxyWarStateEvent ev)
    {
        _states[ev.Planet] = ev;
        StateReceived?.Invoke(ev);
    }
}
