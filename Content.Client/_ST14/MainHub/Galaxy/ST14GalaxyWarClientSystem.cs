using Content.Shared._ST14.MainHub;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// Keeps the last state of the galactic war and the plan of the High Command the server sent,
/// and sends the orders of the High Command
/// </summary>
public sealed class ST14GalaxyWarClientSystem : EntitySystem
{
    private readonly Dictionary<string, ST14GalaxyWarStateEvent> _states = new();

    public ST14GalaxyOrdersEvent? Orders { get; private set; }

    public event Action<ST14GalaxyWarStateEvent>? StateReceived;
    public event Action<ST14GalaxyOrdersEvent>? OrdersReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ST14GalaxyWarStateEvent>(OnState);
        SubscribeNetworkEvent<ST14GalaxyOrdersEvent>(OnOrders);
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

    public void AddMark(ST14WarMark mark)
    {
        RaiseNetworkEvent(new ST14GalaxyMarkAddEvent(mark));
    }

    /// <summary>
    /// Removes one mark, or all of them with -1
    /// </summary>
    public void RemoveMark(int index)
    {
        RaiseNetworkEvent(new ST14GalaxyMarkRemoveEvent(index));
    }

    public void SetOrder(string order)
    {
        RaiseNetworkEvent(new ST14GalaxyOrderTextEvent(order));
    }

    private void OnState(ST14GalaxyWarStateEvent ev)
    {
        _states[ev.Planet] = ev;
        StateReceived?.Invoke(ev);
    }

    private void OnOrders(ST14GalaxyOrdersEvent ev)
    {
        Orders = ev;
        OrdersReceived?.Invoke(ev);
    }
}
