using System.Linq;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Events;
using Content.Shared._ST14.MainHub;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.MainHub;

/// <summary>
/// Every player is bound to one side for the war over a region: chosen in the lobby, or by the first job they get.
/// Until the Federation takes the region whole, only the jobs of that side are open to them
/// </summary>
public sealed partial class ST14GalaxyWarSystem
{
    private readonly Dictionary<NetUserId, ST14GalaxyFaction> _sides = new();

    private void InitializeSides()
    {
        SubscribeNetworkEvent<ST14SideRequestEvent>(OnSideRequest);
        SubscribeNetworkEvent<ST14SideChooseEvent>(OnSideChoose);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnJobCandidates);
        SubscribeLocalEvent<GetDisallowedJobsEvent>(OnDisallowedJobs);
        SubscribeLocalEvent<IsRoleAllowedEvent>(OnRoleAllowed);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    public ST14GalaxyFaction? ChosenSide(NetUserId user)
    {
        return _sides.TryGetValue(user, out var side) ? side : null;
    }

    private bool IsBugJob(ProtoId<JobPrototype> job)
    {
        return _protoMan.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs) && bugs.Jobs.Contains(job);
    }

    /// <summary>
    /// The job belongs to the side the player is bound to, or the player has no side yet
    /// </summary>
    private bool Fits(NetUserId user, ProtoId<JobPrototype> job)
    {
        return ChosenSide(user) is not { } side || IsBugJob(job) == (side == ST14GalaxyFaction.Arachnid);
    }

    private void OnJobCandidates(ref StationJobsGetCandidatesEvent ev)
    {
        for (var i = ev.Jobs.Count - 1; i >= 0; i--)
        {
            if (!Fits(ev.Player, ev.Jobs[i]))
                ev.Jobs.RemoveAt(i);
        }
    }

    private void OnDisallowedJobs(ref GetDisallowedJobsEvent ev)
    {
        if (ChosenSide(ev.Player.UserId) == null)
            return;

        foreach (var job in _protoMan.EnumeratePrototypes<JobPrototype>())
        {
            if (!Fits(ev.Player.UserId, job.ID))
                ev.Jobs.Add(job.ID);
        }
    }

    private void OnRoleAllowed(ref IsRoleAllowedEvent ev)
    {
        if (ev.Jobs is not { } jobs)
            return;

        foreach (var job in jobs)
        {
            if (!Fits(ev.Player.UserId, job))
                ev.Cancelled = true;
        }
    }

    /// <summary>
    /// A player who never chose is bound by the first job they play in the war over the region
    /// </summary>
    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        if (ev.JobId is not { } job || _sides.ContainsKey(ev.Player.UserId) || !_protoMan.HasIndex<JobPrototype>(job))
            return;

        _sides[ev.Player.UserId] = IsBugJob(job) ? ST14GalaxyFaction.Arachnid : ST14GalaxyFaction.Federation;
        Save();
        SendSide(ev.Player);
    }

    private void OnSideRequest(ST14SideRequestEvent ev, EntitySessionEventArgs args)
    {
        SendSide(args.SenderSession);
    }

    private void OnSideChoose(ST14SideChooseEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_sides.ContainsKey(user) && ST14GalaxyWarRules.Sides.Contains(ev.Side))
        {
            _sides[user] = ev.Side;
            Save();
        }

        SendSide(args.SenderSession);
    }

    /// <summary>
    /// The code of the region the war is bound to on the planet fought over now, empty between regions
    /// </summary>
    private string RegionCode()
    {
        foreach (var war in _wars.Values)
        {
            if (war.ActiveRegion >= 0)
                return war.Cells[war.RegionCenters[war.ActiveRegion]].Code;
        }

        return string.Empty;
    }

    private void SendSide(ICommonSession session)
    {
        RaiseNetworkEvent(new ST14SideStateEvent(ChosenSide(session.UserId), RegionCode()), session);
    }

    /// <summary>
    /// The region is taken or the war started over: everyone is free to choose again
    /// </summary>
    private void ClearSides()
    {
        _sides.Clear();
        Save();
        foreach (var session in Listeners())
        {
            SendSide(session);
        }
    }
}
