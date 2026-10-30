using System.Linq;
using System.Text.Json;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Players.JobWhitelist;
using Content.Shared._ST14.MainHub;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ST14.MainHub;

/// <summary>
/// Keeps the war on the war planets of the galaxy map: who holds every hex sector, how far its bar is, and which sector
/// the High Command picked for the next attack. The state is saved in the server data folder and survives restarts.
/// It also counts the dead of the rounds fought in the target sector and keeps the plan the High Command draws on the map.
/// There is no game mode deciding the winner yet, so results come in through <see cref="ApplyResult"/> and the st14_war command
/// </summary>
public sealed partial class ST14GalaxyWarSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private JobWhitelistManager _whitelist = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private NpcFactionSystem _faction = default!;

    private static readonly ResPath SavePath = new("/st14_galaxy_war.json");

    private readonly Dictionary<string, ST14PlanetWar> _wars = new();
    private readonly Dictionary<string, int> _targets = new();
    private readonly List<ST14WarMark> _marks = new();
    private string _order = string.Empty;

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
        SubscribeNetworkEvent<ST14GalaxyMarkAddEvent>(OnMarkAdd);
        SubscribeNetworkEvent<ST14GalaxyMarkRemoveEvent>(OnMarkRemove);
        SubscribeNetworkEvent<ST14GalaxyOrderTextEvent>(OnOrderText);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);

        Load();
    }

    public IEnumerable<string> Planets => _wars.Keys;

    public bool TryGetWar(string planet, out ST14PlanetWar war, out int target)
    {
        target = _targets.GetValueOrDefault(planet, -1);
        return _wars.TryGetValue(planet, out war!);
    }

    /// <summary>
    /// High Command: players on the whitelist of the role, and admins so the map can be tested
    /// </summary>
    public bool CanCommand(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Round) ||
               _whitelist.IsWhitelisted(session.UserId, ST14GalaxyWarRules.HighCommand);
    }

    /// <summary>
    /// Result of a round fought in the target sector of the planet. Called by the game mode once there is one
    /// </summary>
    public bool ApplyResult(string planet, bool federationWon, bool objectives)
    {
        var target = _targets.GetValueOrDefault(planet, -1);
        if (!_wars.TryGetValue(planet, out var war) || target < 0)
            return false;

        war.ApplyResult(target, federationWon, objectives);
        // a taken sector is no longer a target, the High Command picks the next one
        if (!war.Cells[target].Front)
            _targets[planet] = war.FirstLanding ?? -1;

        Changed();
        return true;
    }

    public bool SetTarget(string planet, int cell)
    {
        if (!_wars.TryGetValue(planet, out var war) || cell < 0 || cell >= war.Cells.Length || !war.Cells[cell].Front)
            return false;

        _targets[planet] = cell;
        Changed();
        return true;
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
        Send(args.SenderSession);
        SendOrders(args.SenderSession);
    }

    #region Kills

    private void OnMobState(MobStateChangedEvent ev)
    {
        if (ev.NewMobState != MobState.Dead || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        // every round is fought in the target sector of the war planet for now, there is one war planet
        var (planet, war) = _wars.FirstOrDefault();
        if (war == null)
            return;

        var target = _targets.GetValueOrDefault(planet, -1);
        if (target < 0)
            return;

        if (_faction.IsMemberOfAny(ev.Target, BugFactions))
            war.Cells[target].BugsKilled++;
        else if (HasComp<HumanoidProfileComponent>(ev.Target))
            war.Cells[target].TroopersKilled++;
        else
            return;

        _killsDirty = true;
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

    #region High Command plan

    private bool Commands(EntitySessionEventArgs args)
    {
        if (CanCommand(args.SenderSession))
            return true;

        _sawmill.Warning($"{args.SenderSession.Name} tried to change the plan of the High Command without being in it");
        return false;
    }

    private void OnMarkAdd(ST14GalaxyMarkAddEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args) || _marks.Count >= ST14GalaxyOrdersEvent.MaxMarks || !Enum.IsDefined(ev.Mark.Kind))
            return;

        // marks stay on the table, a little past its rim at most
        var mark = ev.Mark with { From = ClampToMap(ev.Mark.From), To = ClampToMap(ev.Mark.To) };
        if (mark.IsArrow && (mark.To - mark.From).Length() < 0.01f)
            return;

        _marks.Add(mark);
        OrdersChanged();
    }

    private void OnMarkRemove(ST14GalaxyMarkRemoveEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args))
            return;

        if (ev.Index < 0)
            _marks.Clear();
        else if (ev.Index < _marks.Count)
            _marks.RemoveAt(ev.Index);
        else
            return;

        OrdersChanged();
    }

    private void OnOrderText(ST14GalaxyOrderTextEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args))
            return;

        _order = Clean(ev.Order);
        _sawmill.Info($"{args.SenderSession.Name} set the order of the High Command: {_order}");
        OrdersChanged();
    }

    private static Vector2 ClampToMap(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            return Vector2.Zero;

        return point.Length() > 1.1f ? Vector2.Normalize(point) * 1.1f : point;
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
        foreach (var session in _player.Sessions.Where(s => s.Status == SessionStatus.InGame))
        {
            SendOrders(session);
        }
    }

    private void SendOrders(ICommonSession session)
    {
        RaiseNetworkEvent(new ST14GalaxyOrdersEvent(_marks.ToArray(), _order, CanCommand(session)), session);
    }

    #endregion

    private void OnOrder(ST14GalaxyWarOrderEvent ev, EntitySessionEventArgs args)
    {
        if (!CanCommand(args.SenderSession))
        {
            _sawmill.Warning($"{args.SenderSession.Name} tried to order an attack without being in the High Command");
            return;
        }

        if (SetTarget(ev.Planet, ev.Cell))
            _sawmill.Info($"{args.SenderSession.Name} ordered the attack on sector {ev.Cell} of {ev.Planet}");
    }

    private void Changed()
    {
        Save();
        // players still connecting have no whitelist loaded yet, they ask for the state when they open the map
        foreach (var session in _player.Sessions.Where(s => s.Status == SessionStatus.InGame))
        {
            Send(session);
        }
    }

    private void Send(ICommonSession session)
    {
        var command = CanCommand(session);
        foreach (var (planet, war) in _wars)
        {
            RaiseNetworkEvent(new ST14GalaxyWarStateEvent(planet, war.ExportState(), _targets.GetValueOrDefault(planet, -1), command),
                session);
        }
    }

    #region Saving

    private sealed class SaveData
    {
        public Dictionary<string, PlanetSave> Planets { get; set; } = new();
        public List<MarkSave> Marks { get; set; } = new();
        public string Order { get; set; } = string.Empty;
    }

    private sealed class PlanetSave
    {
        public int Target { get; set; } = -1;
        public List<CellSave> Cells { get; set; } = new();
    }

    private sealed class MarkSave
    {
        public ST14WarMarkKind Kind { get; set; }
        public float FromX { get; set; }
        public float FromY { get; set; }
        public float ToX { get; set; }
        public float ToY { get; set; }
    }

    private sealed class CellSave
    {
        public ST14GalaxyFaction Faction { get; set; }
        public float Liberation { get; set; }
        public bool Hive { get; set; }
        public bool Base { get; set; }
        public int BugsKilled { get; set; }
        public int TroopersKilled { get; set; }
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
                war.ImportState(saved.Cells.Select(c => new ST14HexCellState(c.Faction, c.Liberation, c.Hive, c.Base, c.BugsKilled, c.TroopersKilled)).ToList()))
            {
                target = saved.Target >= 0 && saved.Target < war.Cells.Length && war.Cells[saved.Target].Front ? saved.Target : target;
            }

            _wars[planet.ID] = war;
            _targets[planet.ID] = target;
        }

        if (save == null)
            return;

        _order = Clean(save.Order);
        foreach (var mark in save.Marks.Take(ST14GalaxyOrdersEvent.MaxMarks))
        {
            _marks.Add(new ST14WarMark(mark.Kind, new Vector2(mark.FromX, mark.FromY), new Vector2(mark.ToX, mark.ToY)));
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
                Cells = war.ExportState().Select(c => new CellSave
                {
                    Faction = c.Faction,
                    Liberation = c.Liberation,
                    Hive = c.Hive,
                    Base = c.Base,
                    BugsKilled = c.BugsKilled,
                    TroopersKilled = c.TroopersKilled,
                }).ToList(),
            };
        }

        save.Order = _order;
        save.Marks = _marks.Select(m => new MarkSave
        {
            Kind = m.Kind,
            FromX = m.From.X,
            FromY = m.From.Y,
            ToX = m.To.X,
            ToY = m.To.Y,
        }).ToList();

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
