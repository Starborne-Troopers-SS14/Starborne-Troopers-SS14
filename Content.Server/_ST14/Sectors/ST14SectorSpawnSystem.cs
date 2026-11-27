using System.Linq;
using System.Numerics;
using Content.Server._ST14.MainHub;
using Content.Server.GameTicking;
using Content.Server.Stack;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Spawners.Components;
using Content.Shared.Spawners.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Station.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Where players come in on a sector round. Everyone comes in anew, in a fresh kit: nothing of last round's body is
/// kept. A trooper comes in at the usual landing - the job spawn points of the sector their side starts in - or at a
/// bunker base they picked on the landing map when joining. A base lets a trooper in for one soldier supply from its
/// crate, an empty base lets nobody in
/// </summary>
public sealed partial class ST14SectorSpawnSystem : EntitySystem
{
    /// <summary>
    /// The stack the soldier supplies a bunker base spends on every trooper that comes in there
    /// </summary>
    public static readonly ProtoId<StackPrototype> SoldierSupplies = "ST14SoldierSupplies";

    private const string CrateStorage = "storagebase";

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;
    [Dependency] private ST14SharedZLevelsSystem _zLevels = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    private readonly List<EntityCoordinates> _points = new();

    /// <summary>
    /// The base each player picked for their next spawn
    /// </summary>
    private readonly Dictionary<NetUserId, EntityUid> _landing = new();

    /// <summary>
    /// The player being spawned: the spawning event itself does not say who it is
    /// </summary>
    private NetUserId? _spawning;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning, before: new[] { typeof(SpawnPointSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _landing.Clear());
        SubscribeNetworkEvent<ST14LandingRequestEvent>(OnLandingRequest);
        SubscribeNetworkEvent<ST14LandingChooseEvent>(OnLandingChoose);
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        _spawning = ev.Player.UserId;
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        var user = _spawning;
        _spawning = null;

        // players of a round that is starting spawn before it counts as in the round, on a sector round they come here too
        if (args.SpawnResult != null || _ticker.RunLevel != GameRunLevel.InRound && _sectors.Planet == null || args.Job == null)
            return;

        var side = SideOfJob(args.Job.Value);

        // a trooper who picked a bunker base comes in there, for one soldier supply
        if (side == ST14GalaxyFaction.Federation && user is { } id && _landing.Remove(id, out var picked) &&
            BaseSpot(picked) is { } spot && TakeSupply(picked))
        {
            args.SpawnResult = _stationSpawning.SpawnPlayerMob(spot, args.Job, args.HumanoidCharacterProfile, args.Station);
            return;
        }

        var start = _sectors.StartOf(side);
        var map = start >= 0 && _sectors.LoadedMaps.TryGetValue(start, out var uid) ? uid : (EntityUid?) null;

        // only maps that lay out spawn points for the job take part, every other map keeps the stock rule
        // a job no map lays out points for, like the medic, starts with the others of its side
        if (!FindPoints(args, map, false) && !FindPoints(args, map, true) &&
            (map == null || !FindPoints(args, null, false) && !FindPoints(args, null, true)))
        {
            return;
        }

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(_random.Pick(_points), args.Job, args.HumanoidCharacterProfile, args.Station);
    }

    #region Landing points

    /// <summary>
    /// The bunker bases a side can come in at: on the loaded sectors it holds, with soldier supplies left
    /// </summary>
    public List<ST14LandingPoint> LandingPoints(ST14GalaxyFaction side)
    {
        var points = new List<ST14LandingPoint>();
        if (side != ST14GalaxyFaction.Federation || _sectors.Planet is not { } planet || !_war.TryGetWar(planet, out var war, out _))
            return points;

        var query = EntityQueryEnumerator<ST14BunkerBaseComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } level || !_container.TryGetContainer(uid, CrateStorage, out _))
                continue;

            var cell = _sectors.CellOf(level);
            if (cell < 0 || war.Cells[cell].Faction != side)
                continue;

            var supplies = Supplies(uid);
            if (supplies <= 0)
                continue;

            points.Add(new ST14LandingPoint(GetNetEntity(uid), cell, war.Cells[cell].Code, SectorPosition(cell, xform), supplies));
        }

        return points;
    }

    private void OnLandingRequest(ST14LandingRequestEvent ev, EntitySessionEventArgs args)
    {
        var side = _war.ChosenSide(args.SenderSession.UserId) ?? _war.SideOf(args.SenderSession);
        var planet = _sectors.Planet ?? string.Empty;
        var region = -1;
        if (_war.TryGetWar(planet, out var war, out _) && _sectors.Target >= 0)
            region = war.Cells[_sectors.Target].Region;

        RaiseNetworkEvent(new ST14LandingOptionsEvent(planet, region, LandingPoints(side).ToArray()), args.SenderSession);
    }

    private void OnLandingChoose(ST14LandingChooseEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (ev.Point is not { } net || !TryGetEntity(net, out var point) || !HasComp<ST14BunkerBaseComponent>(point))
        {
            _landing.Remove(user);
            return;
        }

        _landing[user] = point.Value;
    }

    /// <summary>
    /// Soldier supplies in the crate of a base
    /// </summary>
    public int Supplies(EntityUid crate)
    {
        if (!_container.TryGetContainer(crate, CrateStorage, out var storage))
            return 0;

        var count = 0;
        foreach (var item in storage.ContainedEntities)
        {
            if (TryComp<StackComponent>(item, out var stack) && stack.StackTypeId == SoldierSupplies)
                count += stack.Count;
        }

        return count;
    }

    private bool TakeSupply(EntityUid crate)
    {
        if (!_container.TryGetContainer(crate, CrateStorage, out var storage))
            return false;

        foreach (var item in storage.ContainedEntities)
        {
            if (!TryComp<StackComponent>(item, out var stack) || stack.StackTypeId != SoldierSupplies || stack.Count <= 0)
                continue;

            _stack.TryUse((item, stack), 1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Where a trooper comes in at a base: the middle of the bunker it stands in, its crate is in a corner
    /// </summary>
    private EntityCoordinates? BaseSpot(EntityUid crate)
    {
        if (TerminatingOrDeleted(crate) || Transform(crate).MapUid is not { } level || _sectors.CellOf(level) < 0)
            return null;

        var xform = Transform(crate);
        return ST14Dugouts.TryGetAt(EntityManager, xform.Coordinates, out _, out var center, out _)
            ? new EntityCoordinates(level, center + new Vector2(0.5f))
            : xform.Coordinates;
    }

    /// <summary>
    /// Where a thing stands in the tiles of its sector, the same on every level of it
    /// </summary>
    private Vector2 SectorPosition(int cell, TransformComponent xform)
    {
        var world = _transform.GetWorldPosition(xform);
        if (_sectors.LevelsOfCell(cell) is { } levels && levels.TryGetValue(0, out var main) && _zLevels.TryGetLayerGrid(main, out var grid))
            return Vector2.Transform(world, _transform.GetInvWorldMatrix(grid.Value.Owner));

        return world;
    }

    #endregion

    private bool FindPoints(PlayerSpawningEvent args, EntityUid? map, bool anyOfSide)
    {
        _points.Clear();
        var side = SideOfJob(args.Job!.Value);
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (point.SpawnType != SpawnPointType.Job || point.Job is not { } job)
                continue;

            if (anyOfSide ? SideOfJob(job) != side : job != args.Job)
                continue;

            if (map != null && xform.MapUid != map)
                continue;

            if (args.Station != null && _station.GetOwningStation(uid, xform) != args.Station)
                continue;

            _points.Add(xform.Coordinates);
        }

        return _points.Count > 0;
    }

    private ST14GalaxyFaction SideOfJob(ProtoId<JobPrototype> job)
    {
        return _prototype.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs) && bugs.Jobs.Contains(job)
            ? ST14GalaxyFaction.Arachnid
            : ST14GalaxyFaction.Federation;
    }
}
