using Content.Server.GameTicking;
using Content.Shared.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Spawners.Components;
using Content.Shared.Spawners.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Late joiners on a sector map come in where their side starts: a bug at the hive's spawn points, a trooper at the
/// landing zone. The stock rule would put everyone who joins late at the same late-join points
/// </summary>
public sealed partial class ST14SectorSpawnSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;

    private readonly List<EntityCoordinates> _points = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawningEvent>(OnPlayerSpawning, before: new[] { typeof(SpawnPointSystem) });
    }

    private void OnPlayerSpawning(PlayerSpawningEvent args)
    {
        if (args.SpawnResult != null || args.Job == null || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        // only maps that lay out spawn points for the job take part, every other map keeps the stock rule
        _points.Clear();
        var query = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var point, out var xform))
        {
            if (point.SpawnType != SpawnPointType.Job || point.Job != args.Job)
                continue;

            if (args.Station != null && _station.GetOwningStation(uid, xform) != args.Station)
                continue;

            _points.Add(xform.Coordinates);
        }

        if (_points.Count == 0)
            return;

        args.SpawnResult = _stationSpawning.SpawnPlayerMob(_random.Pick(_points), args.Job, args.HumanoidCharacterProfile, args.Station);
    }
}
