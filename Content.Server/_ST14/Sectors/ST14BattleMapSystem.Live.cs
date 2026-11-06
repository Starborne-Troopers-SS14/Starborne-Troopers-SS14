using System.Linq;
using System.Numerics;
using Content.Server._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// What moves on the battle map: the icons of the things that have them and the squads. A squad is a few players of one
/// side who see each other on the map, nobody else sees where they are
/// </summary>
public sealed partial class ST14BattleMapSystem
{
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private sealed class Squad
    {
        public int Id;
        public int Number;
        public ST14GalaxyFaction Side;
        public NetUserId Leader;
        public readonly List<NetUserId> Members = new();
    }

    private readonly List<Squad> _squads = new();
    private readonly HashSet<ICommonSession> _open = new();
    private int _nextSquad;
    private TimeSpan _nextLive;

    private void InitializeLive()
    {
        SubscribeNetworkEvent<ST14BattleMapClosedEvent>((_, args) => _open.Remove(args.SenderSession));
        SubscribeNetworkEvent<ST14SquadCreateEvent>(OnSquadCreate);
        SubscribeNetworkEvent<ST14SquadJoinEvent>(OnSquadJoin);
        SubscribeNetworkEvent<ST14SquadLeaveEvent>(OnSquadLeave);
    }

    private void ClearLive()
    {
        _squads.Clear();
        _open.Clear();
        _nextSquad = 0;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_open.Count == 0 || _timing.CurTime < _nextLive)
            return;

        _nextLive = _timing.CurTime + LiveInterval;
        var icons = Icons();
        foreach (var session in _open.ToArray())
        {
            if (session.Status == SessionStatus.Disconnected || !_viewers.TryGetValue(session, out var side))
            {
                _open.Remove(session);
                continue;
            }

            SendLive(session, side, icons);
        }
    }

    private List<(ST14GalaxyFaction? Side, ST14MapIconInfo Info)> Icons()
    {
        var icons = new List<(ST14GalaxyFaction?, ST14MapIconInfo)>();
        var query = EntityQueryEnumerator<ST14MapIconComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out var icon, out var xform, out var meta))
        {
            if (meta.EntityPrototype is not { } proto || Place(xform) is not { } place)
                continue;

            icons.Add((icon.Side, new ST14MapIconInfo(place.Cell, place.Position, proto.ID)));
        }

        return icons;
    }

    /// <summary>
    /// The sector and the place in its tiles of something standing on a sector map
    /// </summary>
    private (int Cell, Vector2 Position)? Place(TransformComponent xform)
    {
        if (CompOrNull<ST14SectorMapComponent>(xform.MapUid) is not { Cell: >= 0 } sector || xform.GridUid is not { } grid)
            return null;

        var world = _transform.GetWorldPosition(xform);
        return (sector.Cell, Vector2.Transform(world, _transform.GetInvWorldMatrix(grid)));
    }

    private void SendLive(ICommonSession session, ST14GalaxyFaction side, List<(ST14GalaxyFaction? Side, ST14MapIconInfo Info)> icons)
    {
        var visible = icons.Where(i => i.Side == null || i.Side == side).Select(i => i.Info).ToArray();
        var squads = _squads.Where(s => s.Side == side)
            .Select(s => new ST14SquadInfo(s.Id, s.Number, NameOf(s.Leader), s.Members.Select(NameOf).ToArray()))
            .ToArray();

        var own = SquadOf(session.UserId);
        var mates = new List<ST14SquadMate>();
        if (own != null && own.Side == side)
        {
            foreach (var member in own.Members)
            {
                if (!_players.TryGetSessionById(member, out var mate) ||
                    mate.AttachedEntity is not { } body ||
                    _mobState.IsDead(body) ||
                    Place(Transform(body)) is not { } place)
                {
                    continue;
                }

                mates.Add(new ST14SquadMate(mate.Name, place.Cell, place.Position, member == own.Leader, member == session.UserId));
            }
        }

        RaiseNetworkEvent(new ST14BattleMapLiveEvent(_ticker.RoundId, visible, squads, own?.Side == side ? own.Id : -1, mates.ToArray()), session);
    }

    private string NameOf(NetUserId user)
    {
        return _players.TryGetSessionById(user, out var session) ? session.Name : "?";
    }

    private Squad? SquadOf(NetUserId user)
    {
        return _squads.FirstOrDefault(s => s.Members.Contains(user));
    }

    private void Leave(NetUserId user)
    {
        if (SquadOf(user) is not { } squad)
            return;

        squad.Members.Remove(user);
        if (squad.Members.Count == 0)
            _squads.Remove(squad);
        else if (squad.Leader == user)
            squad.Leader = squad.Members[0];
    }

    private void OnSquadCreate(ST14SquadCreateEvent ev, EntitySessionEventArgs args)
    {
        if (_sectors.Planet == null || !_viewers.TryGetValue(args.SenderSession, out var side))
            return;

        var user = args.SenderSession.UserId;
        Leave(user);

        // the smallest number no squad of the side uses
        var number = 1;
        while (_squads.Any(s => s.Side == side && s.Number == number))
        {
            number++;
        }

        var squad = new Squad { Id = _nextSquad++, Number = number, Side = side, Leader = user };
        squad.Members.Add(user);
        _squads.Add(squad);
        SendLiveTo(side);
    }

    private void OnSquadJoin(ST14SquadJoinEvent ev, EntitySessionEventArgs args)
    {
        if (!_viewers.TryGetValue(args.SenderSession, out var side) ||
            _squads.FirstOrDefault(s => s.Id == ev.Id) is not { } squad ||
            squad.Side != side ||
            squad.Members.Count >= ST14BattleMapLiveEvent.MaxSquadSize)
        {
            return;
        }

        var user = args.SenderSession.UserId;
        if (squad.Members.Contains(user))
            return;

        Leave(user);
        squad.Members.Add(user);
        SendLiveTo(side);
    }

    private void OnSquadLeave(ST14SquadLeaveEvent ev, EntitySessionEventArgs args)
    {
        Leave(args.SenderSession.UserId);
        if (_viewers.TryGetValue(args.SenderSession, out var side))
            SendLiveTo(side);
    }

    /// <summary>
    /// A squad changed: the side sees it at once instead of after the next tick
    /// </summary>
    private void SendLiveTo(ST14GalaxyFaction side)
    {
        var icons = Icons();
        foreach (var session in _open)
        {
            if (_viewers.TryGetValue(session, out var viewing) && viewing == side)
                SendLive(session, side, icons);
        }
    }
}
