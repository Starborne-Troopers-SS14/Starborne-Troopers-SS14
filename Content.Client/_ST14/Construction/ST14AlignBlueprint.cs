using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14AlignBlueprint : SnapgridCenter
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(0.25);

    private readonly ST14MultitoolSystem _multitool;

    private EntityCoordinates _lastCoords;
    private Direction _lastDirection;
    private TimeSpan _nextCheck;
    private bool _lastValid;

    public ST14AlignBlueprint(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
        _multitool = _entMan.System<ST14MultitoolSystem>();

        ValidPlaceColor = Color.FromHex("#4d8cff");
        InvalidPlaceColor = Color.FromHex("#ff3b3b");
    }

    public override bool IsValidPosition(EntityCoordinates position)
    {
        if (position == _lastCoords && pManager.Direction == _lastDirection && _timing.RealTime < _nextCheck)
            return _lastValid;

        _lastCoords = position;
        _lastDirection = pManager.Direction;
        _nextCheck = _timing.RealTime + RecheckDelay;

        _lastValid = GetPlacementError(position) == null;
        return _lastValid;
    }

    public string? GetPlacementError()
    {
        return GetPlacementError(MouseCoords);
    }

    private string? GetPlacementError(EntityCoordinates position)
    {
        if (pManager.Hijack is not ST14BlueprintPlacementHijack hijack || _player.LocalEntity is not { } user)
            return "st14-blueprint-cant-place";

        return _multitool.GetPlacementError(user, hijack.Buildable, position, pManager.Direction, out _);
    }

    public void Rotate()
    {
        pManager.Rotate();
    }
}
