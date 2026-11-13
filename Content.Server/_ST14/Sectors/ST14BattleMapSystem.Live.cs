using System.Linq;
using System.Numerics;
using Content.Server._ST14.Comms;
using Content.Server._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.Mobs;
using Content.Shared.Roles;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// What moves on the battle map, and the squads. A squad is a few players of one side who see each other on the map:
/// nobody else learns where they are. Members ask others in with the right button on them, or anyone joins from the list
/// </summary>
public sealed partial class ST14BattleMapSystem
{
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InviteTime = TimeSpan.FromSeconds(60);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14SubsectorSystem _subsectors = default!;
    [Dependency] private ST14CommsSystem _comms = default!;

    /// <summary>
    /// Of the troopers only this job forms squads, and it takes the lead of a squad it joins
    /// </summary>
    public static readonly ProtoId<JobPrototype> SquadLeaderJob = "ST14SquadLeader";

    private sealed class Squad
    {
        public int Id;
        public int Number;
        public ST14GalaxyFaction Side;
        public NetUserId Leader;
        public readonly List<NetUserId> Members = new();
    }

    private readonly List<Squad> _squads = new();
    private readonly Dictionary<NetUserId, (int Squad, string From, TimeSpan Until)> _invites = new();
    private readonly HashSet<ICommonSession> _open = new();
    private int _nextSquad;
    private TimeSpan _nextLive;

    private void InitializeLive()
    {
        SubscribeNetworkEvent<ST14BattleMapClosedEvent>((_, args) => _open.Remove(args.SenderSession));
        SubscribeNetworkEvent<ST14SquadRequestEvent>((_, args) => SendSquads(args.SenderSession));
        SubscribeNetworkEvent<ST14SquadCreateEvent>(OnSquadCreate);
        SubscribeNetworkEvent<ST14SquadJoinEvent>(OnSquadJoin);
        SubscribeNetworkEvent<ST14SquadLeaveEvent>(OnSquadLeave);
        SubscribeNetworkEvent<ST14SquadInviteAnswerEvent>(OnInviteAnswer);
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    private void ClearLive()
    {
        _squads.Clear();
        _invites.Clear();
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
        var points = _subsectors.Points(_sectors.LoadedMaps).ToArray();
        foreach (var session in _open.ToArray())
        {
            if (session.Status == SessionStatus.Disconnected || !_viewers.TryGetValue(session, out var side))
            {
                _open.Remove(session);
                continue;
            }

            var visible = icons.Where(i => i.Side == null || i.Side == side).Select(i => i.Info).ToArray();
            RaiseNetworkEvent(new ST14BattleMapLiveEvent(_ticker.RoundId, visible, Mates(session), points), session);
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

    private ST14SquadMate[] Mates(ICommonSession session)
    {
        if (SquadOf(session.UserId) is not { } squad)
            return Array.Empty<ST14SquadMate>();

        var mates = new List<ST14SquadMate>();
        var viewer = session.AttachedEntity;
        foreach (var member in squad.Members)
        {
            if (!_players.TryGetSessionById(member, out var mate) ||
                mate.AttachedEntity is not { } body ||
                _mobState.IsDead(body) ||
                Place(Transform(body)) is not { } place)
            {
                continue;
            }

            // the squad only sees each other where the radio pack of a signaller of theirs carries
            if (member != session.UserId &&
                (viewer == null || !_comms.SquadCovers(squad.Id, viewer.Value) || !_comms.SquadCovers(squad.Id, body)))
            {
                continue;
            }

            mates.Add(new ST14SquadMate(mate.Name, place.Cell, place.Position, member == squad.Leader, member == session.UserId));
        }

        return mates.ToArray();
    }

    #region Squads

    private string NameOf(NetUserId user)
    {
        return _players.TryGetSessionById(user, out var session) ? session.Name : "?";
    }

    private ST14SquadMemberStatus StatusOf(NetUserId user)
    {
        if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } body)
            return ST14SquadMemberStatus.Away;

        if (_mobState.IsDead(body))
            return ST14SquadMemberStatus.Dead;

        return _mobState.IsCritical(body) ? ST14SquadMemberStatus.Critical : ST14SquadMemberStatus.Alive;
    }

    private Squad? SquadOf(NetUserId user)
    {
        return _squads.FirstOrDefault(s => s.Members.Contains(user));
    }

    /// <summary>
    /// Id of the squad of the player in the body, null without one
    /// </summary>
    public int? SquadIdOf(EntityUid body)
    {
        return TryComp<ActorComponent>(body, out var actor) ? SquadOf(actor.PlayerSession.UserId)?.Id : null;
    }

    private bool IsSquadLeader(NetUserId user)
    {
        return _players.TryGetSessionById(user, out var session) &&
               session.AttachedEntity is { } body &&
               _mind.TryGetMind(body, out var mindId, out _) &&
               _jobs.MindTryGetJobId(mindId, out var job) &&
               job == SquadLeaderJob;
    }

    /// <summary>
    /// Bugs form their packs as they like, of the troopers only the squad leaders form squads
    /// </summary>
    private bool CanCreate(ICommonSession session)
    {
        return SideOf(session) != ST14GalaxyFaction.Federation || IsSquadLeader(session.UserId);
    }

    private ST14SquadInfo Info(Squad squad)
    {
        var members = squad.Members.Select(m => new ST14SquadMember(NameOf(m), StatusOf(m), m == squad.Leader)).ToArray();
        return new ST14SquadInfo(squad.Id, squad.Number, members);
    }

    private void SendSquads(ICommonSession session)
    {
        if (_sectors.Planet == null)
        {
            RaiseNetworkEvent(new ST14SquadStateEvent(false, _ticker.RoundId, Array.Empty<ST14SquadInfo>(), -1, null), session);
            return;
        }

        var side = SideOf(session);
        var own = SquadOf(session.UserId);
        ST14SquadInvite? invite = null;
        if (_invites.TryGetValue(session.UserId, out var asked) && asked.Until > _timing.CurTime &&
            _squads.FirstOrDefault(s => s.Id == asked.Squad) is { } inviting && inviting.Side == side)
        {
            invite = new ST14SquadInvite(inviting.Id, inviting.Number, asked.From);
        }

        var squads = _squads.Where(s => s.Side == side).OrderBy(s => s.Number).Select(Info).ToArray();
        RaiseNetworkEvent(new ST14SquadStateEvent(true, _ticker.RoundId, squads, own?.Side == side ? own.Id : -1, invite, CanCreate(session)), session);
    }

    /// <summary>
    /// A squad changed: every player in the round learns it, each about the squads of their own side
    /// </summary>
    private void SquadsChanged()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status == SessionStatus.InGame)
                SendSquads(session);
        }
    }

    private void Leave(NetUserId user)
    {
        if (SquadOf(user) is not { } squad)
            return;

        squad.Members.Remove(user);
        if (squad.Members.Count == 0)
            _squads.Remove(squad);
        else if (squad.Leader == user)
            squad.Leader = squad.Members.FirstOrDefault(IsSquadLeader, squad.Members[0]);
    }

    private bool Join(ICommonSession session, int id)
    {
        var side = SideOf(session);
        if (_squads.FirstOrDefault(s => s.Id == id) is not { } squad ||
            squad.Side != side ||
            squad.Members.Contains(session.UserId) ||
            squad.Members.Count >= ST14SquadStateEvent.MaxSquadSize)
        {
            return false;
        }

        Leave(session.UserId);
        squad.Members.Add(session.UserId);
        _invites.Remove(session.UserId);

        // a squad leader takes the lead from a trooper
        if (squad.Side == ST14GalaxyFaction.Federation && IsSquadLeader(session.UserId) && !IsSquadLeader(squad.Leader))
            squad.Leader = session.UserId;

        return true;
    }

    private void OnSquadCreate(ST14SquadCreateEvent ev, EntitySessionEventArgs args)
    {
        if (_sectors.Planet == null)
            return;

        var session = args.SenderSession;
        var side = SideOf(session);
        if (!CanCreate(session))
        {
            if (session.AttachedEntity is { } body)
                _popup.PopupEntity(Loc.GetString("st14-squad-create-leader-only"), body, body, PopupType.Medium);

            return;
        }

        Leave(session.UserId);

        // the smallest number no squad of the side uses
        var number = 1;
        while (_squads.Any(s => s.Side == side && s.Number == number))
        {
            number++;
        }

        var squad = new Squad { Id = _nextSquad++, Number = number, Side = side, Leader = session.UserId };
        squad.Members.Add(session.UserId);
        _squads.Add(squad);
        SquadsChanged();
    }

    private void OnSquadJoin(ST14SquadJoinEvent ev, EntitySessionEventArgs args)
    {
        if (Join(args.SenderSession, ev.Id))
            SquadsChanged();
    }

    private void OnSquadLeave(ST14SquadLeaveEvent ev, EntitySessionEventArgs args)
    {
        Leave(args.SenderSession.UserId);
        SquadsChanged();
    }

    private void OnInviteAnswer(ST14SquadInviteAnswerEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_invites.Remove(user, out var invite))
            return;

        if (ev.Accept && invite.Until > _timing.CurTime)
            Join(args.SenderSession, invite.Squad);

        SquadsChanged();
    }

    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        // the squad sees a member go down or get up
        if (_squads.Count > 0 && TryComp<ActorComponent>(ev.Target, out var actor) && SquadOf(actor.PlayerSession.UserId) != null)
            SquadsChanged();
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (_sectors.Planet == null ||
            args.User == args.Target ||
            !TryComp<ActorComponent>(args.User, out var userActor) ||
            !TryComp<ActorComponent>(args.Target, out var targetActor))
        {
            return;
        }

        var user = userActor.PlayerSession;
        var target = targetActor.PlayerSession;
        var side = SideOf(user);
        if (SideOf(target) != side)
            return;

        var mine = SquadOf(user.UserId);
        var theirs = SquadOf(target.UserId);

        if (mine != null && theirs != mine && mine.Members.Count < ST14SquadStateEvent.MaxSquadSize)
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("st14-squad-verb-invite", ("number", mine.Number)),
                Act = () => Invite(user, target, mine),
            });
        }

        if (theirs != null && theirs != mine && theirs.Members.Count < ST14SquadStateEvent.MaxSquadSize)
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("st14-squad-verb-join", ("number", theirs.Number)),
                Act = () =>
                {
                    if (Join(user, theirs.Id))
                        SquadsChanged();
                },
            });
        }
    }

    private void Invite(ICommonSession from, ICommonSession to, Squad squad)
    {
        if (!_squads.Contains(squad) || !squad.Members.Contains(from.UserId))
            return;

        _invites[to.UserId] = (squad.Id, from.Name, _timing.CurTime + InviteTime);
        if (to.AttachedEntity is { } body)
            _popup.PopupEntity(Loc.GetString("st14-squad-invited", ("from", from.Name), ("number", squad.Number)), body, body, PopupType.Medium);

        if (from.AttachedEntity is { } inviter)
            _popup.PopupEntity(Loc.GetString("st14-squad-invite-sent", ("name", to.Name)), inviter, inviter);

        SendSquads(to);
    }

    #endregion
}
