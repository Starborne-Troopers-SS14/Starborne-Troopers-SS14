using System.Linq;
using System.Text.Json;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Players.JobWhitelist;
using Content.Server.Preferences.Managers;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.MainHub;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Events;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mind;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ST14.MainHub;

/// <summary>
/// Keeps the war on the war planets of the galaxy map: who holds every hex sector, whose turn it is, and which sector
/// the command of that side picked for the next battle. The state is saved in the server data folder and survives restarts.
/// It also counts the dead of the rounds and keeps the plans the commands of both sides draw on the map.
/// Results come in from the sector rounds through <see cref="ApplyResult"/> and from the st14_war command
/// </summary>
public sealed partial class ST14GalaxyWarSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private JobWhitelistManager _whitelist = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedJobSystem _jobs = default!;

    private static readonly ResPath SavePath = new("/st14_galaxy_war.json");

    private readonly Dictionary<string, ST14PlanetWar> _wars = new();
    private readonly Dictionary<string, int> _targets = new();
    private readonly Dictionary<string, int> _stagings = new();
    private readonly Dictionary<string, List<ST14WarLogEntry>> _logs = new();

    /// <summary>
    /// Planets whose target was picked by the command that has the turn
    /// </summary>
    private readonly HashSet<string> _picked = new();

    /// <summary>
    /// Planets a round is being fought on right now and when its time runs out
    /// </summary>
    private readonly Dictionary<string, TimeSpan> _fights = new();

    /// <summary>
    /// The log keeps this many battles, a long war drops the oldest
    /// </summary>
    private const int MaxLog = 300;
    private readonly Dictionary<ST14GalaxyFaction, Plan> _plans = ST14GalaxyWarRules.Sides.ToDictionary(s => s, _ => new Plan());

    /// <summary>
    /// Arrows, marks and the standing order of one command
    /// </summary>
    private sealed class Plan
    {
        public readonly List<ST14WarMark> Marks = new();
        public string Order = string.Empty;
    }

    // deaths come in bursts, the save and the update to the clients wait a little
    private static readonly TimeSpan KillFlush = TimeSpan.FromSeconds(5);
    private TimeSpan _killTimer;
    private bool _killsDirty;

    /// <summary>
    /// NPC factions counted as bugs when they die, everything humanoid is counted as a trooper
    /// </summary>
    private static readonly ProtoId<NpcFactionPrototype>[] BugFactions = { "Xeno" };
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("st14.war");

        SubscribeNetworkEvent<ST14GalaxyWarRequestEvent>(OnRequest);
        SubscribeNetworkEvent<ST14GalaxyWarOrderEvent>(OnOrder);
        SubscribeNetworkEvent<ST14GalaxyWarStagingEvent>(OnStaging);
        SubscribeNetworkEvent<ST14GalaxyMarkAddEvent>(OnMarkAdd);
        SubscribeNetworkEvent<ST14GalaxyMarkRemoveEvent>(OnMarkRemove);
        SubscribeNetworkEvent<ST14GalaxyOrderTextEvent>(OnOrderText);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        InitializeSides();

        Load();
    }

    public IEnumerable<string> Planets => _wars.Keys;

    public bool TryGetWar(string planet, out ST14PlanetWar war, out int target)
    {
        target = _targets.GetValueOrDefault(planet, -1);
        return _wars.TryGetValue(planet, out war!);
    }

    /// <summary>
    /// Command of a side: players on the whitelist of its role, and admins for both sides so the map can be tested
    /// </summary>
    public bool CanCommand(ICommonSession session, ST14GalaxyFaction side)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Round) ||
               _whitelist.IsWhitelisted(session.UserId, ST14GalaxyWarRules.CommandOf(side));
    }

    /// <summary>
    /// Side a player fights for: the bugs when the high priority job of the selected character belongs to the bug lobby,
    /// the Federation otherwise. A member of a command always belongs to its side
    /// </summary>
    public ST14GalaxyFaction SideOf(ICommonSession session)
    {
        if (_whitelist.IsWhitelisted(session.UserId, ST14GalaxyWarRules.HiveCommand))
            return ST14GalaxyFaction.Arachnid;

        if (ChosenSide(session.UserId) is { } chosen)
            return chosen;

        // a player without characters has no selected one, the indexer would throw
        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs) ||
            !prefs.Characters.TryGetValue(prefs.SelectedCharacterIndex, out var character) ||
            !_protoMan.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs))
        {
            return ST14GalaxyFaction.Federation;
        }

        var high = character.JobPriorities.Where(j => j.Value == JobPriority.High).Select(j => j.Key);
        return high.Any(job => bugs.Jobs.Contains(job)) ? ST14GalaxyFaction.Arachnid : ST14GalaxyFaction.Federation;
    }

    /// <summary>
    /// The sector the attacker gathers in for the battle over the target: what its command picked, or else the first
    /// fit neighbour. -1 when there is none, then the attacker lands in the target itself
    /// </summary>
    public int StagingOf(string planet)
    {
        var target = _targets.GetValueOrDefault(planet, -1);
        if (!_wars.TryGetValue(planet, out var war) || target < 0)
            return -1;

        var options = war.StagingOptions(target).ToList();
        if (options.Count == 0)
            return -1;

        var picked = _stagings.GetValueOrDefault(planet, -1);
        return options.Contains(picked) ? picked : options.FirstOrDefault(c => war.Cells[c].Initialized, options[0]);
    }

    /// <summary>
    /// Result of the round fought over the target sector of the planet. A null winner is a battle that ran out of time.
    /// The initiative goes to the other side and its command picks the next battle
    /// </summary>
    public bool ApplyResult(string planet, ST14GalaxyFaction? winner, IReadOnlyDictionary<int, ST14GalaxyFaction?>? points = null)
    {
        var target = _targets.GetValueOrDefault(planet, -1);
        if (!_wars.TryGetValue(planet, out var war) || target < 0)
            return false;

        var log = _logs.GetOrNew(planet);
        log.Add(new ST14WarLogEntry(_ticker.RoundId, target, ST14PlanetWar.Enemy(war.Cells[target].Faction), winner));
        if (log.Count > MaxLog)
            log.RemoveAt(0);

        // the region is taken whole: the war moves on and every player picks a side again
        if (war.ApplyResult(target, StagingOf(planet), winner, points))
            ClearSides();

        _fights.Remove(planet);
        _picked.Remove(planet);
        _stagings.Remove(planet);
        _targets[planet] = war.Cells[target].Front ? target : war.FirstLanding ?? -1;

        Changed();
        return true;
    }

    /// <summary>
    /// A round started on the target of the planet: until it ends the battle stays what it is, the result goes to it
    /// </summary>
    public void BeginBattle(string planet, TimeSpan endsAt)
    {
        if (_wars.TryGetValue(planet, out var war))
        {
            war.LockRegion(_targets.GetValueOrDefault(planet, -1));
            war.RefreshFront();
        }

        _fights[planet] = endsAt;
        Changed();
    }

    public bool SetTarget(string planet, int cell)
    {
        if (_fights.ContainsKey(planet) || !_wars.TryGetValue(planet, out var war) || cell < 0 || cell >= war.Cells.Length || !war.Cells[cell].Front)
            return false;

        _targets[planet] = cell;
        _picked.Add(planet);
        _stagings.Remove(planet);
        Changed();
        return true;
    }

    public bool SetStaging(string planet, int cell)
    {
        var target = _targets.GetValueOrDefault(planet, -1);
        if (_fights.ContainsKey(planet) || !_wars.TryGetValue(planet, out var war) || target < 0 || !war.StagingOptions(target).Contains(cell))
            return false;

        _stagings[planet] = cell;
        Changed();
        return true;
    }

    /// <summary>
    /// A sector was saved at the end of a round: from now on it loads every round
    /// </summary>
    public void MarkInitialized(string planet, int cell)
    {
        if (!_wars.TryGetValue(planet, out var war) || cell < 0 || cell >= war.Cells.Length || war.Cells[cell].Initialized)
            return;

        war.Cells[cell].Initialized = true;
        war.RefreshFront();
        Changed();
    }

    /// <summary>
    /// Starts the war on the planet over from the state made by its seed
    /// </summary>
    public bool Reset(string planet)
    {
        if (!_protoMan.TryIndex<ST14GalaxyPlanetPrototype>(planet, out var proto) || !proto.War)
            return false;

        var war = Create(proto);
        _wars[planet] = war;
        _targets[planet] = war.FirstLanding ?? -1;
        _stagings.Remove(planet);
        _logs.Remove(planet);
        _picked.Remove(planet);
        _fights.Remove(planet);

        var ev = new ST14WarResetEvent(planet);
        RaiseLocalEvent(ref ev);
        ClearSides();

        Changed();
        return true;
    }

    private ST14PlanetWar Create(ST14GalaxyPlanetPrototype planet)
    {
        var name = Loc.GetString(planet.Name);
        return new ST14PlanetWar(planet, name.Length > 0 ? name[..1].ToUpperInvariant() : "X");
    }

    private void OnRequest(ST14GalaxyWarRequestEvent ev, EntitySessionEventArgs args)
    {
        Resend(args.SenderSession);
    }

    /// <summary>
    /// Sends the war and the plans again, after the commands this player belongs to changed
    /// </summary>
    public void Resend(ICommonSession session)
    {
        Send(session);
        SendOrders(session);
    }

    #region Kills

    /// <summary>
    /// Sector the current round is fought in, fixed when the round starts: the High Command may pick the next target
    /// during the round, the dead of this round still count where they fell
    /// </summary>
    private (string Planet, int Cell)? _battle;

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        _battle = PickBattle();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _battle = null;

        // a round ended without a result, by an admin for one: the choice opens again
        if (_fights.Count > 0)
        {
            _fights.Clear();
            Changed();
        }
    }

    private (string Planet, int Cell)? PickBattle()
    {
        if (CurrentPlanet() is not { } planet)
            return null;

        var target = _targets.GetValueOrDefault(planet, -1);
        return target < 0 ? null : (planet, target);
    }

    private void OnMobState(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        // a war loaded in the middle of a round still gets its battle sector, once
        _battle ??= PickBattle();
        if (_battle is not { } battle || !_wars.TryGetValue(battle.Planet, out var war) || battle.Cell >= war.Cells.Length)
            return;

        // the dead count in the sector they fell in, a round can spread over several
        var target = battle.Cell;
        if (CompOrNull<ST14SectorMapComponent>(Transform(ev.Target).MapUid) is { } map && map.Planet == battle.Planet && map.Cell < war.Cells.Length)
            target = map.Cell;

        if (_faction.IsMemberOfAny(ev.Target, BugFactions))
            war.Cells[target].BugsKilled++;
        else if (IsTrooper(ev.Target))
            war.Cells[target].TroopersKilled++;
        else
            return;

        _killsDirty = true;
    }

    /// <summary>
    /// A trooper is a player in a Federation role: a humanoid with a mind that holds a job outside the bug lobby.
    /// NPCs, ghosts and jobless humanoids do not count
    /// </summary>
    private bool IsTrooper(EntityUid uid)
    {
        if (!HasComp<HumanoidProfileComponent>(uid) ||
            !_mind.TryGetMind(uid, out var mindId, out _) ||
            !_jobs.MindTryGetJobId(mindId, out var job) ||
            job is not { } jobId)
        {
            return false;
        }

        return !_protoMan.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs) || !bugs.Jobs.Contains(jobId);
    }

    /// <summary>
    /// War planet of the current round: the one set in st14.war_planet, or the only war planet when there is one.
    /// Null when it cannot be told, then deaths are not counted rather than counted on the wrong planet
    /// </summary>
    public string? CurrentPlanet()
    {
        var planet = _cfg.GetCVar(ST14CVars.WarPlanet);
        if (!string.IsNullOrEmpty(planet))
            return _wars.ContainsKey(planet) ? planet : null;

        return _wars.Count == 1 ? _wars.Keys.First() : null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_killsDirty)
            return;

        _killTimer += TimeSpan.FromSeconds(frameTime);
        if (_killTimer < KillFlush)
            return;

        _killTimer = TimeSpan.Zero;
        _killsDirty = false;
        Changed();
    }

    #endregion

    #region Plans of the commands

    private bool Commands(EntitySessionEventArgs args, ST14GalaxyFaction side)
    {
        if (_plans.ContainsKey(side) && CanCommand(args.SenderSession, side))
            return true;

        _sawmill.Warning($"{args.SenderSession.Name} tried to change the plan of the {side} command without being in it");
        return false;
    }

    private void OnMarkAdd(ST14GalaxyMarkAddEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args, ev.Side) || !Enum.IsDefined(ev.Mark.Kind))
            return;

        var plan = _plans[ev.Side];
        if (plan.Marks.Count >= ST14GalaxyOrdersEvent.MaxMarks)
            return;

        // marks stay on the table, a little past its rim at most
        var mark = ev.Mark.Surface
            ? ev.Mark with { From = ClampToSphere(ev.Mark.From), To = ClampToSphere(ev.Mark.To) }
            : ev.Mark with { From = ClampToMap(ev.Mark.From), To = ClampToMap(ev.Mark.To) };
        if (mark.IsArrow && (mark.To - mark.From).Length() < 0.01f)
            return;

        plan.Marks.Add(mark);
        OrdersChanged();
    }

    private void OnMarkRemove(ST14GalaxyMarkRemoveEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args, ev.Side))
            return;

        var marks = _plans[ev.Side].Marks;
        if (ev.Index < 0)
            marks.Clear();
        else if (ev.Index < marks.Count)
            marks.RemoveAt(ev.Index);
        else
            return;

        OrdersChanged();
    }

    private void OnOrderText(ST14GalaxyOrderTextEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args, ev.Side))
            return;

        var plan = _plans[ev.Side];
        plan.Order = Clean(ev.Order);
        _sawmill.Info($"{args.SenderSession.Name} set the order of the {ev.Side} command: {plan.Order}");
        OrdersChanged();
    }

    private static Vector2 ClampToMap(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            return Vector2.Zero;

        return point.Length() > 1.1f ? Vector2.Normalize(point) * 1.1f : point;
    }

    private static Vector2 ClampToSphere(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            return Vector2.Zero;

        return new Vector2(Math.Clamp(point.X, -MathF.PI, MathF.PI), Math.Clamp(point.Y, -MathF.PI / 2, MathF.PI / 2));
    }

    private static string Clean(string? order)
    {
        if (string.IsNullOrWhiteSpace(order))
            return string.Empty;

        var line = order.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return line.Length > ST14GalaxyOrdersEvent.MaxOrderLength ? line[..ST14GalaxyOrdersEvent.MaxOrderLength] : line;
    }

    private void OrdersChanged()
    {
        Save();
        foreach (var session in Listeners())
        {
            SendOrders(session);
        }
    }

    private string[] MembersOf(ST14GalaxyFaction side)
    {
        var role = ST14GalaxyWarRules.CommandOf(side);
        return Listeners().Where(s => _whitelist.IsWhitelisted(s.UserId, role)).Select(s => s.Name).OrderBy(n => n).ToArray();
    }

    /// <summary>
    /// A player sees the plan of their own side and of every command they belong to, never the plan of the enemy
    /// </summary>
    private void SendOrders(ICommonSession session)
    {
        var own = SideOf(session);
        foreach (var (side, plan) in _plans)
        {
            var command = CanCommand(session, side);
            // a hidden plan still goes out empty, so a player who just left a command loses its plan on the map too
            if (side != own && !command)
            {
                RaiseNetworkEvent(new ST14GalaxyOrdersEvent(side, Array.Empty<ST14WarMark>(), string.Empty, false, Array.Empty<string>()), session);
                continue;
            }

            RaiseNetworkEvent(new ST14GalaxyOrdersEvent(side, plan.Marks.ToArray(), plan.Order, command, MembersOf(side)), session);
        }
    }

    #endregion
    private void OnOrder(ST14GalaxyWarOrderEvent ev, EntitySessionEventArgs args)
    {
        if (!_wars.TryGetValue(ev.Planet, out var war) || !CanCommand(args.SenderSession, war.Turn))
        {
            _sawmill.Warning($"{args.SenderSession.Name} tried to order an attack without having the turn");
            return;
        }

        if (SetTarget(ev.Planet, ev.Cell))
            _sawmill.Info($"{args.SenderSession.Name} ordered the battle for sector {ev.Cell} of {ev.Planet}");
    }

    private void OnStaging(ST14GalaxyWarStagingEvent ev, EntitySessionEventArgs args)
    {
        if (!_wars.TryGetValue(ev.Planet, out var war) || !CanCommand(args.SenderSession, war.Turn))
        {
            _sawmill.Warning($"{args.SenderSession.Name} tried to pick a staging sector without having the turn");
            return;
        }

        if (SetStaging(ev.Planet, ev.Cell))
            _sawmill.Info($"{args.SenderSession.Name} picked sector {ev.Cell} of {ev.Planet} to gather in");
    }

    private void Changed()
    {
        Save();
        foreach (var session in Listeners())
        {
            Send(session);
        }
    }

    /// <summary>
    /// Players the war and the plans go out to: in the round and in the main menu, where the galaxy map is open
    /// (the menu is the Connected status). Players still connecting have no whitelist loaded yet,
    /// they ask for the state when they open the map
    /// </summary>
    private IEnumerable<ICommonSession> Listeners()
    {
        return _player.Sessions.Where(s => s.Status is SessionStatus.Connected or SessionStatus.InGame);
    }

    private void Send(ICommonSession session)
    {
        SendSide(session);
        foreach (var (planet, war) in _wars)
        {
            RaiseNetworkEvent(new ST14GalaxyWarStateEvent(planet, war.ExportState(), _targets.GetValueOrDefault(planet, -1), StagingOf(planet),
                war.Turn, CanCommand(session, war.Turn), _logs.GetValueOrDefault(planet)?.ToArray() ?? Array.Empty<ST14WarLogEntry>(),
                _picked.Contains(planet), _fights.ContainsKey(planet),
                _fights.TryGetValue(planet, out var ends) ? (float) (ends - _timing.CurTime).TotalSeconds : 0f, war.ActiveRegion), session);
        }
    }

    #region Saving

    private sealed class SaveData
    {
        public Dictionary<string, PlanetSave> Planets { get; set; } = new();
        public Dictionary<ST14GalaxyFaction, PlanSave> Plans { get; set; } = new();

        /// <summary>
        /// The side each player chose for the war over the current region
        /// </summary>
        public Dictionary<Guid, ST14GalaxyFaction> Sides { get; set; } = new();
    }

    private sealed class PlanSave
    {
        public List<MarkSave> Marks { get; set; } = new();
        public string Order { get; set; } = string.Empty;
    }

    private sealed class PlanetSave
    {
        public int Target { get; set; } = -1;
        public int Staging { get; set; } = -1;
        public bool TargetPicked { get; set; }
        public ST14GalaxyFaction Turn { get; set; } = ST14GalaxyFaction.Federation;
        public int ActiveRegion { get; set; } = -1;
        public List<LogSave> Log { get; set; } = new();
        public List<CellSave> Cells { get; set; } = new();
    }

    private sealed class LogSave
    {
        public int Round { get; set; }
        public int Cell { get; set; }
        public ST14GalaxyFaction Attacker { get; set; }
        public ST14GalaxyFaction? Winner { get; set; }
    }

    private sealed class MarkSave
    {
        public ST14WarMarkKind Kind { get; set; }
        public float FromX { get; set; }
        public float FromY { get; set; }
        public float ToX { get; set; }
        public float ToY { get; set; }
        public bool Surface { get; set; }
    }

    private sealed class CellSave
    {
        public ST14GalaxyFaction Faction { get; set; }
        public float Liberation { get; set; }
        public bool Hive { get; set; }
        public bool Base { get; set; }
        public int BugsKilled { get; set; }
        public int TroopersKilled { get; set; }
        public bool Initialized { get; set; }
    }

    private void Load()
    {
        SaveData? save = null;
        try
        {
            if (_resources.UserData.Exists(SavePath))
            {
                using var reader = _resources.UserData.OpenText(SavePath);
                save = JsonSerializer.Deserialize<SaveData>(reader.ReadToEnd());
            }
        }
        catch (Exception e)
        {
            _sawmill.Error($"Could not read the galactic war save, starting over: {e}");
        }

        foreach (var planet in _protoMan.EnumeratePrototypes<ST14GalaxyPlanetPrototype>().Where(p => p.War))
        {
            var war = Create(planet);
            var target = war.FirstLanding ?? -1;

            if (save != null && save.Planets.TryGetValue(planet.ID, out var saved) &&
                war.ImportState(saved.Cells.Select(c => new ST14HexCellState(c.Faction, c.Liberation, c.Hive, c.Base, c.BugsKilled, c.TroopersKilled, c.Initialized)).ToList(), saved.Turn, saved.ActiveRegion))
            {
                target = saved.Target >= 0 && saved.Target < war.Cells.Length && war.Cells[saved.Target].Front ? saved.Target : war.FirstLanding ?? -1;
                if (saved.Staging >= 0)
                    _stagings[planet.ID] = saved.Staging;

                if (saved.TargetPicked)
                    _picked.Add(planet.ID);

                _logs[planet.ID] = saved.Log.Select(l => new ST14WarLogEntry(l.Round, l.Cell, l.Attacker, l.Winner)).ToList();
            }

            _wars[planet.ID] = war;
            _targets[planet.ID] = target;
        }

        if (save == null)
            return;

        foreach (var (user, side) in save.Sides)
        {
            _sides[new NetUserId(user)] = side;
        }

        foreach (var (side, saved) in save.Plans)
        {
            if (!_plans.TryGetValue(side, out var plan))
                continue;

            plan.Order = Clean(saved.Order);
            foreach (var mark in saved.Marks.Take(ST14GalaxyOrdersEvent.MaxMarks))
            {
                plan.Marks.Add(new ST14WarMark(mark.Kind, new Vector2(mark.FromX, mark.FromY), new Vector2(mark.ToX, mark.ToY), mark.Surface));
            }
        }
    }

    private void Save()
    {
        var save = new SaveData();
        foreach (var (planet, war) in _wars)
        {
            save.Planets[planet] = new PlanetSave
            {
                Target = _targets.GetValueOrDefault(planet, -1),
                Staging = _stagings.GetValueOrDefault(planet, -1),
                TargetPicked = _picked.Contains(planet),
                Turn = war.Turn,
                ActiveRegion = war.ActiveRegion,
                Log = _logs.GetValueOrDefault(planet)?.Select(l => new LogSave { Round = l.Round, Cell = l.Cell, Attacker = l.Attacker, Winner = l.Winner }).ToList() ?? new(),
                Cells = war.ExportState().Select(c => new CellSave
                {
                    Faction = c.Faction,
                    Liberation = c.Liberation,
                    Hive = c.Hive,
                    Base = c.Base,
                    BugsKilled = c.BugsKilled,
                    TroopersKilled = c.TroopersKilled,
                    Initialized = c.Initialized,
                }).ToList(),
            };
        }

        foreach (var (user, side) in _sides)
        {
            save.Sides[user.UserId] = side;
        }

        foreach (var (side, plan) in _plans)
        {
            save.Plans[side] = new PlanSave
            {
                Order = plan.Order,
                Marks = plan.Marks.Select(m => new MarkSave
                {
                    Kind = m.Kind,
                    FromX = m.From.X,
                    FromY = m.From.Y,
                    ToX = m.To.X,
                    ToY = m.To.Y,
                    Surface = m.Surface,
                }).ToList(),
            };
        }

        try
        {
            using var writer = _resources.UserData.OpenWriteText(SavePath);
            writer.Write(JsonSerializer.Serialize(save));
        }
        catch (Exception e)
        {
            _sawmill.Error($"Could not save the galactic war: {e}");
        }
    }

    #endregion
}

/// <summary>
/// The war on a planet started over: its saved sector maps go
/// </summary>
[ByRefEvent]
public readonly record struct ST14WarResetEvent(string Planet);

/// <summary>
/// Marks the map of a sector loaded for this round
/// </summary>
[RegisterComponent]
public sealed partial class ST14SectorMapComponent : Component
{
    [DataField]
    public string Planet = string.Empty;

    [DataField]
    public int Cell = -1;
}
