using System.Linq;
using Content.Server._ST14.MainHub;
using Content.Server.Decals;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.CCVar;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Events;
using Content.Shared.Ghost.Components;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles.Jobs;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Rounds of the war on a planet. A round loads the map of the sector being fought over, the map of the sector the
/// attacker gathers in and the saved maps of every sector fought over before, each on a map of its own. It ends when a
/// side is wiped out on all of them or when the time runs out, and then every loaded sector is written back to its save
/// </summary>
public sealed partial class ST14SectorSystem : EntitySystem
{
    private const string SettingsId = "Default";

    /// <summary>
    /// The game map of a sector round: it only makes the station, the sectors come after it
    /// </summary>
    private static readonly ProtoId<GameMapPrototype> AnchorMap = "ST14SectorWar";

    private static readonly ResPath SaveRoot = new("/st14_sectors");

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ServerStationSystem _station = default!;

    /// <summary>
    /// The anchor grid of this round, null when the round is not a sector round
    /// </summary>
    private EntityUid? _anchor;

    private string? _planet;
    private int _target = -1;
    private int _staging = -1;

    /// <summary>
    /// The maps of the sectors loaded this round, by cell
    /// </summary>
    private readonly Dictionary<int, EntityUid> _maps = new();

    private bool _troopersSeen;
    private bool _hiveSeen;
    private bool _decided;
    private TimeSpan _nextCheck;
    private TimeSpan _timeUp;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
        SubscribeLocalEvent<PostGameMapLoad>(OnPostMapLoad);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<ST14WarResetEvent>(OnWarReset);
    }

    private ST14SectorSettingsPrototype Settings => _prototype.Index<ST14SectorSettingsPrototype>(SettingsId);

    public IReadOnlyDictionary<int, EntityUid> LoadedMaps => _maps;

    public string? Planet => _planet;

    /// <summary>
    /// The sector a side starts this round in, -1 outside a sector round
    /// </summary>
    public int StartOf(ST14GalaxyFaction side)
    {
        if (_planet == null || _target < 0 || !_war.TryGetWar(_planet, out var war, out _))
            return -1;

        return war.StartOf(side, _target, _staging);
    }

    #region Loading

    /// <summary>
    /// The war and the sector the next round is fought over, false when the next round is not a sector round
    /// </summary>
    public bool NextBattle(out string planet, out int cell)
    {
        cell = -1;
        planet = string.Empty;

        // an admin who picked the map by hand, or turned sector rounds off, gets the stock rotation
        if (!_cfg.GetCVar(ST14CVars.SectorRounds) || !string.IsNullOrEmpty(_cfg.GetCVar(CCVars.GameMap)))
            return false;

        planet = _war.CurrentPlanet() ?? string.Empty;
        return planet.Length > 0 && _war.TryGetWar(planet, out var war, out cell) && cell >= 0 && cell < war.Cells.Length;
    }

    private void OnLoadingMaps(LoadingMapsEvent ev)
    {
        if (!NextBattle(out _, out _))
            return;

        ev.Maps.Clear();
        ev.Maps.Add(_prototype.Index(AnchorMap));
    }

    private void OnPostMapLoad(PostGameMapLoad ev)
    {
        if (ev.GameMap.ID == AnchorMap.Id && ev.Grids.Count > 0)
            _anchor = ev.Grids[0];
    }

    /// <summary>
    /// The stations exist by now, and nobody has spawned yet
    /// </summary>
    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (_anchor is not { } anchor || !NextBattle(out var planet, out var target))
            return;

        _planet = planet;
        _target = target;
        _staging = _war.StagingOf(planet);
        _troopersSeen = false;
        _hiveSeen = false;
        _decided = false;

        if (_station.GetOwningStation(anchor) is not { } station || !_war.TryGetWar(planet, out var war, out _))
        {
            Log.Error($"The sector round on {planet} has no station to join its sectors to");
            return;
        }

        var cells = new HashSet<int> { target };
        if (_staging >= 0)
            cells.Add(_staging);

        for (var i = 0; i < war.Cells.Length; i++)
        {
            if (war.Cells[i].Initialized)
                cells.Add(i);
        }

        foreach (var cell in cells)
        {
            LoadCell(planet, cell, war.Cells[cell].Terrain, station);
        }

        Log.Info($"Sector round on {planet}: battle for {target}, gathering in {_staging}, {_maps.Count} sectors loaded");
    }

    private void LoadCell(string planet, int cell, int terrain, EntityUid station)
    {
        var save = SavePath(planet, cell);
        var saved = _resources.UserData.Exists(save);

        Entity<MapComponent>? map = null;
        HashSet<Entity<MapGridComponent>>? grids = null;

        // the references a save dropped to things outside its map are expected, the save is whole otherwise
        if (saved && !_mapLoader.TryLoadMap(save, out map, out grids, new DeserializationOptions { LogInvalidEntities = false }))
        {
            Log.Error($"The save of sector {cell} of {planet} does not load, the sector starts from a fresh map");
            saved = false;
        }

        if (!saved)
        {
            if (FreshMap(terrain) is not { } file)
            {
                Log.Error($"Terrain {terrain} has no sector map");
                return;
            }

            if (!_mapLoader.TryLoadMap(file, out map, out grids, new DeserializationOptions { InitializeMaps = true }))
            {
                Log.Error($"The sector map {file} does not load");
                return;
            }
        }

        var comp = EnsureComp<ST14SectorMapComponent>(map!.Value);
        comp.Planet = planet;
        comp.Cell = cell;
        _maps[cell] = map.Value;

        foreach (var grid in grids!)
        {
            _station.AddGridToStation(station, grid);
        }

        if (saved)
            ClearTrash(map.Value);
        else
            PlaceQueens(grids!);
    }

    /// <summary>
    /// One of the maps made for the terrain, at random: once fought over the sector keeps it in its save
    /// </summary>
    private ResPath? FreshMap(int terrain)
    {
        var kind = _prototype.EnumeratePrototypes<ST14SectorTerrainPrototype>().FirstOrDefault(t => t.Index == terrain && t.Maps.Count > 0);
        return kind == null ? null : _random.Pick(kind.Maps);
    }

    private void PlaceQueens(HashSet<Entity<MapGridComponent>> grids)
    {
        var settings = Settings;
        // the queen comes with the hive, a server without it leaves the places empty
        if (settings.Queen is not { } queen || !_prototype.HasIndex<EntityPrototype>(queen))
            return;

        var slots = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<ST14SectorQueenSlotComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } grid && grids.Any(g => g.Owner == grid))
                slots.Add(xform.Coordinates);
        }

        _random.Shuffle(slots);
        foreach (var slot in slots.Take(settings.Queens))
        {
            Spawn(queen, slot);
        }
    }

    /// <summary>
    /// A save keeps everything; what is of no use next round goes when it loads: corpses, ghosts, blood and casings.
    /// The minds the living bodies had are gone too, the bodies wait for their owners to come back to them
    /// </summary>
    private void ClearTrash(EntityUid map)
    {
        var settings = Settings;
        var mapId = Transform(map).MapID;
        var trash = new List<EntityUid>();

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId && _mobState.IsDead(uid))
                trash.Add(uid);
        }

        var ghosts = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        while (ghosts.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId)
                trash.Add(uid);
        }

        var casings = EntityQueryEnumerator<CartridgeAmmoComponent, TransformComponent>();
        while (casings.MoveNext(out var uid, out var casing, out var xform))
        {
            if (xform.MapID == mapId && casing.Spent && xform.ParentUid == xform.GridUid)
                trash.Add(uid);
        }

        foreach (var name in settings.TrashComponents)
        {
            var type = Factory.GetRegistration(name).Type;
            foreach (var (uid, _) in EntityManager.GetAllComponents(type))
            {
                if (Transform(uid).MapID == mapId)
                    trash.Add(uid);
            }
        }

        foreach (var uid in trash)
        {
            QueueDel(uid);
        }

        var minds = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (minds.MoveNext(out var uid, out var container, out var xform))
        {
            if (xform.MapID == mapId)
                _mind.ClearSavedMind((uid, container));
        }

        ClearTrashDecals(mapId, settings);
    }

    private void ClearTrashDecals(MapId mapId, ST14SectorSettingsPrototype settings)
    {
        var tags = settings.TrashDecalTags.ToHashSet();
        var remove = new List<DecalIndex>();

        var grids = EntityQueryEnumerator<DecalGridComponent, TransformComponent>();
        while (grids.MoveNext(out var grid, out var decals, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            remove.Clear();
            foreach (var (index, chunk) in decals.ChunkCollection.ChunkCollection)
            {
                foreach (var (id, decal) in chunk.Decals)
                {
                    if (_prototype.TryIndex<DecalPrototype>(decal.Id, out var proto) && proto.Tags.Any(tags.Contains))
                        remove.Add(new DecalIndex(index, id));
                }
            }

            foreach (var index in remove)
            {
                _decals.RemoveDecal(grid, index);
            }
        }
    }

    #endregion

    #region Round end

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New == GameRunLevel.InRound)
        {
            var settings = Settings;
            _nextCheck = _timing.CurTime + settings.GracePeriod;
            _timeUp = _timing.CurTime + settings.RoundLimit;
        }

        // the round is over, however it ended: every sector it touched goes back to its save
        if (ev.New == GameRunLevel.PostRound)
            SaveAll();
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _anchor = null;
        _planet = null;
        _target = -1;
        _staging = -1;
        _maps.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_planet is not { } planet || _decided || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + Settings.CheckInterval;

        if (_timing.CurTime >= _timeUp)
        {
            EndBattle(planet, null, "st14-sector-time-up");
            return;
        }

        CountSides(out var troopers, out var hive);
        _troopersSeen |= troopers > 0;
        _hiveSeen |= hive > 0;

        // a side that never showed up cannot lose, there was nobody to fight
        if (!_troopersSeen || !_hiveSeen || troopers > 0 && hive > 0)
            return;

        EndBattle(planet,
            hive == 0 ? ST14GalaxyFaction.Federation : ST14GalaxyFaction.Arachnid,
            hive == 0 ? "st14-sector-troopers-won" : "st14-sector-hive-won");
    }

    private void EndBattle(string planet, ST14GalaxyFaction? winner, string message)
    {
        _decided = true;
        _war.ApplyResult(planet, winner);
        _ticker.EndRound(Loc.GetString(message));
    }

    /// <summary>
    /// Live fighters of both sides on all the sectors of the round: troopers are people with a player behind them, the
    /// hive is every live mob of the hive's faction, players or not: a queen keeps laying larvae while she lives
    /// </summary>
    private void CountSides(out int troopers, out int hive)
    {
        troopers = 0;
        hive = 0;
        var faction = Settings.HiveFaction;
        var maps = _maps.Values.ToHashSet();

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } map || !maps.Contains(map) || _mobState.IsDead(uid))
                continue;

            if (TryComp<NpcFactionMemberComponent>(uid, out var member) && _faction.IsMember((uid, member), faction))
                hive++;
            else if (HasComp<HumanoidProfileComponent>(uid) && TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
                troopers++;
        }
    }

    #endregion

    #region Saves

    private static ResPath SavePath(string planet, int cell)
    {
        return SaveRoot / $"{planet}_{cell}.yml";
    }

    /// <summary>
    /// Writes every loaded sector over its save, whoever won and whichever sector the battle was for
    /// </summary>
    public void SaveAll()
    {
        if (_planet is not { } planet)
            return;

        MarkOwners();
        _resources.UserData.CreateDir(SaveRoot);

        foreach (var (cell, map) in _maps)
        {
            if (TerminatingOrDeleted(map))
                continue;

            // minds and the station stay out of the save: they belong to the round, not to the map
            var options = new SerializationOptions
            {
                MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
                LogAutoInclude = null,
            };

            try
            {
                if (_mapLoader.TrySaveMap(map, SavePath(planet, cell), options))
                    _war.MarkInitialized(planet, cell);
                else
                    Log.Error($"Could not save sector {cell} of {planet}");
            }
            catch (Exception e)
            {
                Log.Error($"Could not save sector {cell} of {planet}: {e}");
            }
        }
    }

    /// <summary>
    /// Writes on every body with a player whose it is: next round the player comes back to it
    /// </summary>
    private void MarkOwners()
    {
        var maps = _maps.Values.ToHashSet();
        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var container, out var xform))
        {
            if (xform.MapUid is not { } map || !maps.Contains(map) ||
                !_mind.TryGetMind(uid, out var mindId, out var mind, container) || mind.UserId is not { } user)
            {
                continue;
            }

            var owner = EnsureComp<ST14SectorOwnerComponent>(uid);
            owner.UserId = user.UserId;
            _jobs.MindTryGetJobId(mindId, out var job);
            owner.Job = job?.Id;
        }
    }

    private void OnWarReset(ref ST14WarResetEvent ev)
    {
        if (!_resources.UserData.IsDir(SaveRoot))
            return;

        foreach (var name in _resources.UserData.DirectoryEntries(SaveRoot).ToList())
        {
            if (name.StartsWith($"{ev.Planet}_"))
                _resources.UserData.Delete(SaveRoot / name);
        }
    }

    #endregion
}
