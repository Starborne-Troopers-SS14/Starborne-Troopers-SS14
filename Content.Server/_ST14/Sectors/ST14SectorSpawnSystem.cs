using System.Linq;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Spawners.Components;
using Content.Shared.Spawners.EntitySystems;
using Content.Shared.Station.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Where players come in on a sector round: back in the body they left at the end of the last round, or else at the job
/// spawn points of the sector their side starts in. The stock rule would put everyone who joins late at the same points
/// </summary>
public sealed partial class ST14SectorSpawnSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    private readonly List<EntityCoordinates> _points = new();

    /// <summary>
    /// The player being spawned: the spawning event itself does not say who it is
    /// </summary>
    private string? _spawning;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning, before: new[] { typeof(SpawnPointSystem) });
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        _spawning = ev.Player.UserId.UserId.ToString();
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        var user = _spawning;
        _spawning = null;

        // players of a round that is starting spawn before it counts as in the round, on a sector round they come here too
        if (args.SpawnResult != null || _ticker.RunLevel != GameRunLevel.InRound && _sectors.Planet == null)
            return;

        if (user is { } id && OwnBody(id, args.Job) is { } body)
        {
            args.SpawnResult = body;
            return;
        }

        if (args.Job == null)
            return;

        var start = _sectors.StartOf(SideOfJob(args.Job.Value));
        var map = start >= 0 && _sectors.LoadedMaps.TryGetValue(start, out var uid) ? uid : (EntityUid?) null;

        // ST14: troopers come back to life in a forward base built in a bunker of their sector, if there is one
        if (SideOfJob(args.Job.Value) == ST14GalaxyFaction.Federation && FindBases(map))
        {
            args.SpawnResult = _stationSpawning.SpawnPlayerMob(_random.Pick(_points), args.Job, args.HumanoidCharacterProfile, args.Station);
            return;
        }

        // only maps that lay out spawn points for the job take part, every other map keeps the stock rule
        // a job no map lays out points for, like the medic, starts with the others of its side
        if (!FindPoints(args, map, false) && !FindPoints(args, map, true) &&
            (map == null || !FindPoints(args, null, false) && !FindPoints(args, null, true)))
        {
            return;
        }

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(_random.Pick(_points), args.Job, args.HumanoidCharacterProfile, args.Station);
    }

    /// <summary>
    /// ST14: the forward bases (bunker upgrades) standing on the levels of the sector <paramref name="map"/>, or of any
    /// loaded sector when it is not known
    /// </summary>
    private bool FindBases(EntityUid? map)
    {
        _points.Clear();
        var zLevels = EntityManager.System<Content.Shared._ST14.ZLevels.Core.EntitySystems.ST14SharedZLevelsSystem>();
        var query = EntityQueryEnumerator<Content.Shared._ST14.ZLevels.Fortifications.ST14BunkerBaseComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid is not { } level)
                continue;

            // the base stands under the surface: the sector is the surface over it
            var sector = zLevels.TryMapOffset(level, 1, out var above) ? above.Owner : level;
            if (map != null ? sector != map : !_sectors.LoadedMaps.Values.Contains(sector))
                continue;

            // the middle of the bunker the base is on: its supply crate stands in a corner
            _points.Add(Content.Shared._ST14.ZLevels.Fortifications.ST14Dugouts.TryGetAt(EntityManager, xform.Coordinates, out _, out var center, out _)
                ? new EntityCoordinates(level, center + new System.Numerics.Vector2(0.5f))
                : xform.Coordinates);
        }

        return _points.Count > 0;
    }

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

    /// <summary>
    /// The living body the player left in a sector last round, if it waits for the same job and nobody took it
    /// </summary>
    private EntityUid? OwnBody(string user, ProtoId<JobPrototype>? job)
    {
        var query = EntityQueryEnumerator<ST14SectorOwnerComponent, MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var owner, out var mind, out var xform))
        {
            if (owner.UserId != user || owner.Job != job?.Id || mind.HasMind || _mobState.IsDead(uid))
                continue;

            if (xform.MapUid is { } map && _sectors.LoadedMaps.Values.Contains(map))
                return uid;
        }

        return null;
    }

    private ST14GalaxyFaction SideOfJob(ProtoId<JobPrototype> job)
    {
        return _prototype.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs) && bugs.Jobs.Contains(job)
            ? ST14GalaxyFaction.Arachnid
            : ST14GalaxyFaction.Federation;
    }
}
