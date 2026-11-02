using System.Numerics;
using Content.Server._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// The passages on the six edges of a sector map: a mob that walks into one comes out at the opposite passage of the
/// neighbouring sector, if that sector is loaded this round. It comes out a few tiles in, clear of the passage
/// </summary>
public sealed partial class ST14SectorGateSystem : EntitySystem
{
    private const float ArrivalDepth = 5f;
    private static readonly TimeSpan PopupDelay = TimeSpan.FromSeconds(2);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _lastPopup = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14SectorGateComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(Entity<ST14SectorGateComponent> ent, ref StartCollideEvent args)
    {
        var mob = args.OtherEntity;
        if (!HasComp<MobStateComponent>(mob))
            return;

        if (Destination(ent, out var message) is not { } destination)
        {
            if (message != null)
                Tell(mob, message);
            return;
        }

        // whoever is being dragged comes along
        var pulled = CompOrNull<PullerComponent>(mob)?.Pulling;
        _transform.SetCoordinates(mob, destination);
        if (pulled is { } other)
            _transform.SetCoordinates(other, destination);
    }

    /// <summary>
    /// Where the gate leads, null with the reason when it leads nowhere
    /// </summary>
    public EntityCoordinates? Destination(Entity<ST14SectorGateComponent> gate, out string? message)
    {
        message = null;
        if (_sectors.Planet is not { } planet ||
            CompOrNull<ST14SectorMapComponent>(Transform(gate).MapUid) is not { } here ||
            !_war.TryGetWar(planet, out var war, out _) || here.Cell < 0 || here.Cell >= war.Cells.Length)
        {
            return null;
        }

        var edges = war.Grid.Edges[here.Cell];
        if (gate.Comp.Edge < 0 || gate.Comp.Edge >= edges.Length)
        {
            message = Loc.GetString("st14-sector-gate-closed");
            return null;
        }

        var next = edges[gate.Comp.Edge];
        if (!_sectors.LoadedMaps.TryGetValue(next, out var map))
        {
            message = Loc.GetString("st14-sector-gate-not-initialized", ("code", war.Cells[next].Code));
            return null;
        }

        var back = Array.IndexOf(war.Grid.Edges[next], here.Cell);
        var query = EntityQueryEnumerator<ST14SectorGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var other, out var xform))
        {
            if (xform.MapUid != map || other.Edge != back)
                continue;

            // a step inside the map from the middle of the edge
            var angle = MathF.PI / 3 * other.Edge;
            var inward = -new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ArrivalDepth;
            return xform.Coordinates.Offset(inward);
        }

        message = Loc.GetString("st14-sector-gate-closed");
        return null;
    }

    private void Tell(EntityUid mob, string message)
    {
        var now = _timing.CurTime;
        if (_lastPopup.TryGetValue(mob, out var last) && now - last < PopupDelay)
            return;

        _lastPopup[mob] = now;
        _popup.PopupEntity(message, mob, mob, PopupType.MediumCaution);
    }
}
