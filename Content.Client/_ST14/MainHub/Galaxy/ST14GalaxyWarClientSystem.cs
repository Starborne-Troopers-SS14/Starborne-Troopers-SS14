using Content.Shared._ST14.MainHub;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// Keeps the last state of the galactic war and the plans of the commands the server sent,
/// and sends the orders of the commands
/// </summary>
public sealed class ST14GalaxyWarClientSystem : EntitySystem
{
    private readonly Dictionary<string, ST14GalaxyWarStateEvent> _states = new();

    private readonly Dictionary<ST14GalaxyFaction, ST14GalaxyOrdersEvent> _orders = new();

    /// <summary>
    /// Plans of the sides this player may see: their own and those of the commands they belong to
    /// </summary>
    public IReadOnlyDictionary<ST14GalaxyFaction, ST14GalaxyOrdersEvent> Orders => _orders;

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

    public void AddMark(ST14GalaxyFaction side, ST14WarMark mark)
    {
        RaiseNetworkEvent(new ST14GalaxyMarkAddEvent(side, mark));
    }

    /// <summary>
    /// Removes one mark, or all of them with -1
    /// </summary>
    public void RemoveMark(ST14GalaxyFaction side, int index)
    {
        RaiseNetworkEvent(new ST14GalaxyMarkRemoveEvent(side, index));
    }

    public void SetOrder(ST14GalaxyFaction side, string order)
    {
        RaiseNetworkEvent(new ST14GalaxyOrderTextEvent(side, order));
    }

    private void OnState(ST14GalaxyWarStateEvent ev)
    {
        _states[ev.Planet] = ev;
        StateReceived?.Invoke(ev);
    }

    private void OnOrders(ST14GalaxyOrdersEvent ev)
    {
        _orders[ev.Side] = ev;
        OrdersReceived?.Invoke(ev);
    }
}
