using Content.Server._ST14.Tiles;
using Content.Shared.Gravity;
using Content.Server._ST14.ZLevels.Layers; // ST14: z-levels of the sectors
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
    [Dependency] private ST14MultiTileSystem _multiTile = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ST14SubsectorSystem _subsectors = default!;
    [Dependency] private ServerStationSystem _station = default!;
    [Dependency] private ST14ZLayerSystem _layers = default!;
    [Dependency] private ST14ZNetworkSaveSystem _zSave = default!;

    /// <summary>
    /// The z-levels of every loaded sector by cell, then by depth; depth 0 is the sector map itself
    /// </summary>
    private readonly Dictionary<int, Dictionary<int, EntityUid>> _levels = new();

    /// <summary>
    /// The sector map (depth 0) of every level of a loaded sector, itself included
    /// </summary>
    private readonly Dictionary<EntityUid, EntityUid> _mainOf = new();

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

    /// <summary>
    /// Every level of every loaded sector: the sector maps and the z-levels around them
    /// </summary>
    public IEnumerable<EntityUid> AllLevels => _mainOf.Keys;

    /// <summary>
    /// The levels of the sector a map belongs to (the map itself when it is no sector level)
    /// </summary>
    public IEnumerable<EntityUid> LevelsOf(EntityUid map)
    {
        // read from the z-network, so any map with levels (a sector or one an admin gave levels) works the same
        return _layers.GetLevels(map).Values;
    }

    /// <summary>
    /// The main map (depth 0) of the z-network a map is in, the map itself when it has no levels
    /// </summary>
    public EntityUid MainLevelOf(EntityUid map)
    {
        return _layers.GetLevels(map).GetValueOrDefault(0, map);
    }

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

        // an admin who picked another map by hand, or turned sector rounds off, gets the stock rotation; picking the
        // sector war map itself is asking for a sector round, without its sectors it is a lone grid in space
        var forced = _cfg.GetCVar(CCVars.GameMap);
        if (!_cfg.GetCVar(ST14CVars.SectorRounds) || !string.IsNullOrEmpty(forced) && forced != AnchorMap.Id)
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

        // the ground of the Federation stands every round, the hive's sleeps in its save until it is fought over
        for (var i = 0; i < war.Cells.Length; i++)
        {
            if (war.Cells[i].Initialized && war.Cells[i].Faction == ST14GalaxyFaction.Federation)
                cells.Add(i);
        }

        foreach (var cell in cells)
        {
            LoadCell(planet, cell, war.Cells[cell].Terrain, station, war.Cells[cell].Faction == ST14GalaxyFaction.Arachnid);
        }

        Log.Info($"Sector round on {planet}: battle for {target}, gathering in {_staging}, {_maps.Count} sectors loaded");
    }

    private void LoadCell(string planet, int cell, int terrain, EntityUid station, bool hiveHeld)
    {
        var save = SavePath(planet, cell);
        var saved = false;

        Entity<MapComponent>? map = null;
        HashSet<Entity<MapGridComponent>>? grids = null;
        Dictionary<int, EntityUid>? savedLevels = null;

        // ST14: a sector is saved with all its z-levels, one folder per sector
        var folder = LevelsFolder(planet, cell);
        if (_zSave.HasSave(folder))
        {
            if (_zSave.TryLoadNetwork(folder, out var loaded, new DeserializationOptions { LogInvalidEntities = false }))
            {
                map = loaded[0];
                grids = GridsOn(loaded[0]);
                savedLevels = loaded.ToDictionary(kv => kv.Key, kv => kv.Value.Owner);
                saved = true;
            }
            else
            {
                Log.Error($"The levels of sector {cell} of {planet} do not load, trying the old save");
            }
        }

        // a save from before the z-levels: one file with the sector map only, the levels around it are built fresh
        // the references a save dropped to things outside its map are expected, the save is whole otherwise
        if (!saved && _resources.UserData.Exists(save))
        {
            if (_mapLoader.TryLoadMap(save, out map, out grids, new DeserializationOptions { LogInvalidEntities = false }))
                saved = true;
            else
                Log.Error($"The save of sector {cell} of {planet} does not load, the sector starts from a fresh map");
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

        // a sector is a planet surface: gravity is always on, no generator needed
        EnsureGravity(map.Value);
        foreach (var grid in grids!)
        {
            _station.AddGridToStation(station, grid);
            EnsureGravity(grid);

            // a sector is loaded on its own, not as the game map: the big floors are put together here
            _multiTile.FixGrid(grid);
        }

        // ST14: the z-levels of the sector, from its save or built fresh: -2 bug tunnels, -1 trenches, +1 roofs, +2 sky
        // (ST14ZLayerSystem gives every level the gravity of the sector map)
        var levels = Settings.Layers is { } layerSet
            ? _layers.EnsureLayers(map.Value, layerSet, savedLevels)
            : new Dictionary<int, EntityUid> { [0] = map.Value };

        _levels[cell] = levels;
        foreach (var level in levels.Values)
        {
            _mainOf[level] = map.Value;
        }

        if (saved)
        {
            foreach (var level in levels.Values)
            {
                ClearTrash(level);
            }
        }
        else
        {
            PlaceQueens(grids!);
        }

        _subsectors.PreparePoints(map.Value, grids!, cell, hiveHeld, !saved);
    }

    private void EnsureGravity(EntityUid uid)
    {
        var gravity = EnsureComp<GravityComponent>(uid);
        if (gravity.Inherent && gravity.Enabled)
            return;

        gravity.Inherent = true;
        gravity.Enabled = true;
        var ev = new GravityChangedEvent(uid, true);
        RaiseLocalEvent(uid, ref ev, true);
        Dirty(uid, gravity);
    }

    /// <summary>
    /// The grids standing on a map (the map itself too when it is a planet map)
    /// </summary>
    private HashSet<Entity<MapGridComponent>> GridsOn(EntityUid map)
    {
        var grids = new HashSet<Entity<MapGridComponent>>();
        var query = AllEntityQuery<MapGridComponent, TransformComponent>(); // paused maps too
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapUid == map)
                grids.Add((uid, grid));
        }

        return grids;
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

            if (_planet != null)
                _war.BeginBattle(_planet, _timeUp);
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
        _levels.Clear();
        _mainOf.Clear();
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
        _war.ApplyResult(planet, winner, _subsectors.Holders(_maps));
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
        var maps = _mainOf.Keys.ToHashSet(); // ST14: every z-level of every sector

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

    /// <summary>
    /// The save of a sector from before the z-levels: the sector map alone. Read once and replaced by the folder
    /// </summary>
    private static ResPath SavePath(string planet, int cell)
    {
        return SaveRoot / $"{planet}_{cell}.yml";
    }

    /// <summary>
    /// ST14: the save of a sector with all its z-levels, see ST14ZNetworkSaveSystem
    /// </summary>
    private static ResPath LevelsFolder(string planet, int cell)
    {
        return SaveRoot / $"{planet}_{cell}";
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

        var unsaved = AllowLivingMobs();
        try
        {
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
                    // ST14: the sector map and every z-level around it, into the folder of the sector
                    if (_zSave.TrySaveNetwork(map, LevelsFolder(planet, cell), options))
                    {
                        _war.MarkInitialized(planet, cell);

                        // the old one-file save is superseded by the folder
                        var legacy = SavePath(planet, cell);
                        if (_resources.UserData.Exists(legacy))
                            _resources.UserData.Delete(legacy);
                    }
                    else
                    {
                        Log.Error($"Could not save sector {cell} of {planet}");
                    }
                }
                catch (Exception e)
                {
                    Log.Error($"Could not save sector {cell} of {planet}: {e}");
                }
            }
        }
        finally
        {
            foreach (var proto in unsaved)
            {
                proto.MapSavable = false;
            }
        }
    }

    /// <summary>
    /// Mobs and much of what they are made of are marked to never go into a map file, so a mapper's save does not catch
    /// a player. A sector save wants the living with all they carry, so for the time of the save it lets those through.
    /// Returns the prototypes to mark again
    /// </summary>
    private HashSet<EntityPrototype> AllowLivingMobs()
    {
        var unsaved = new HashSet<EntityPrototype>();
        var maps = _mainOf.Keys.ToHashSet(); // ST14: every z-level of every sector
        var strip = Settings.UnsaveableComponents.Select(name => Factory.GetRegistration(name).Type).ToList();

        void Allow(EntityUid uid, TransformComponent xform)
        {
            if (MetaData(uid).EntityPrototype is { MapSavable: false } proto && unsaved.Add(proto))
                proto.MapSavable = true;

            var children = xform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                Allow(child, Transform(child));
            }
        }

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } map || !maps.Contains(map) || _mobState.IsDead(uid))
                continue;

            Allow(uid, xform);
            foreach (var type in strip)
            {
                if (HasComp(uid, type))
                    RemComp(uid, type);
            }
        }

        return unsaved;
    }

    /// <summary>
    /// Writes on every body with a player whose it is: next round the player comes back to it
    /// </summary>
    private void MarkOwners()
    {
        var maps = _mainOf.Keys.ToHashSet(); // ST14: every z-level of every sector
        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var container, out var xform))
        {
            if (xform.MapUid is not { } map || !maps.Contains(map) ||
                !_mind.TryGetMind(uid, out var mindId, out var mind, container) || mind.UserId is not { } user)
            {
                continue;
            }

            var owner = EnsureComp<ST14SectorOwnerComponent>(uid);
            owner.UserId = user.UserId.ToString();
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
