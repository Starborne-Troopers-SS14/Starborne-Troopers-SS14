using Content.Shared._ST14.Sectors;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The landing map a player sees when joining a sector round: the region of the battle with every place their side
/// can come in at besides the usual landing (bunker bases with soldier supplies left). With no such place there is
/// nothing to choose, and the player joins at once
/// </summary>
public sealed partial class ST14LandingClientSystem : EntitySystem
{
    /// <summary>
    /// How long to wait for the server to tell the landing points before joining the usual way
    /// </summary>
    private static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(3);

    [Dependency] private IGameTiming _timing = default!;

    private Action? _join;
    private TimeSpan _askedAt;
    private ST14LandingWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14LandingOptionsEvent>(OnOptions);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
    }

    /// <summary>
    /// The player chose a job to join as: they pick where to come in first, then <paramref name="join"/> runs
    /// </summary>
    public void BeforeJoin(Action join)
    {
        _window?.Close();
        _join = join;
        _askedAt = _timing.RealTime;
        RaiseNetworkEvent(new ST14LandingRequestEvent());
    }

    private void OnOptions(ST14LandingOptionsEvent ev)
    {
        if (_join == null)
            return;

        if (ev.Points.Length == 0)
        {
            Land(null);
            return;
        }

        _window = new ST14LandingWindow(ev);
        _window.OnPicked += Land;
        _window.OnClose += () =>
        {
            // closed without a choice: the player stays in the lobby
            _window = null;
            _join = null;
        };
        _window.OpenCentered();
    }

    private void Land(NetEntity? point)
    {
        var join = _join;
        _join = null;
        RaiseNetworkEvent(new ST14LandingChooseEvent(point));

        if (_window is { } window)
        {
            _window = null;
            window.Close();
        }

        join?.Invoke();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // no word from the server (no sector round): the usual landing
        if (_join != null && _window == null && _timing.RealTime - _askedAt > AnswerWait)
            Land(null);
    }
}
