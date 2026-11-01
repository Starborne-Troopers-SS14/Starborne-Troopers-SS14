using System.Linq;
using System.Numerics;
using System.Text.Json;
using Content.Server._ST14.MainHub;
using Content.Server.GameTicking;
using Content.Shared.Maps;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.Sectors;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Rounds fought on the sectors of the war planet: the round loads the map of the sector the High Command targets,
/// puts the hive queens in, ends once the troopers or the hive are wiped out and moves the sector's bar.
/// While a sector is being fought over, what was built on its map stays for the next round there
/// </summary>
public sealed partial class ST14SectorSystem : EntitySystem
{
    private const string SettingsId = "Default";
    private static readonly ResPath SaveRoot = new("/st14_sectors");

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;

    /// <summary>
    /// The sector this round is fought on, null when the round is not a sector round
    /// </summary>
    private (string Planet, int Cell, MapId Map)? _sector;

    /// <summary>
    /// Grids the sector map brought, in load order. What stays is written down by its grid in this list
    /// </summary>
    private readonly List<EntityUid> _grids = new();

    /// <summary>
    /// The sector was taken this round: nothing of it is kept
    /// </summary>
    private bool _captured;

    private bool _troopersSeen;
    private bool _hiveSeen;
    private bool _decided;
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LoadingMapsEvent>(OnLoadingMaps);
        SubscribeLocalEvent<PostGameMapLoad>(OnPostMapLoad);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<ST14SectorResultEvent>(OnSectorResult);
    }

    private ST14SectorSettingsPrototype Settings => _prototype.Index<ST14SectorSettingsPrototype>(SettingsId);

    #region Map

    /// <summary>
    /// The game map the target sector of the war is fought on, null when there is no war target or no map for it
    /// </summary>
    public GameMapPrototype? SectorMap(out string planet, out int cell)
    {
        cell = -1;
        planet = string.Empty;

        // an admin who picked the map by hand, or turned sector rounds off, gets the stock rotation
        if (!_cfg.GetCVar(ST14CVars.SectorRounds) || !string.IsNullOrEmpty(_cfg.GetCVar(CCVars.GameMap)))
            return null;

        planet = _war.CurrentPlanet() ?? string.Empty;
        if (planet.Length == 0 || !_war.TryGetWar(planet, out var war, out cell) || cell < 0 || cell >= war.Cells.Length)
            return null;

        var terrain = war.Cells[cell].Terrain;
        var kind = _prototype.EnumeratePrototypes<ST14SectorTerrainPrototype>().FirstOrDefault(t => t.Index == terrain && t.Maps.Count > 0);
        if (kind == null)
            return null;

        // the sector number picks the variant: the same sector always gets the same map
        var map = kind.Maps[(int) ((uint) (cell * 2654435761u) % (uint) kind.Maps.Count)];
        return _prototype.TryIndex<GameMapPrototype>(map, out var proto) ? proto : null;
    }

    private void OnLoadingMaps(LoadingMapsEvent ev)
    {
        if (SectorMap(out _, out _) is not { } map)
            return;

        ev.Maps.Clear();
        ev.Maps.Add(map);
    }

    private void OnPostMapLoad(PostGameMapLoad ev)
    {
        if (SectorMap(out var planet, out var cell) is not { } map || map.ID != ev.GameMap.ID)
            return;

        _sector = (planet, cell, ev.Map);
        _grids.Clear();
        _grids.AddRange(ev.Grids.OrderBy(g => g.Id));
        _captured = false;
        _troopersSeen = false;
        _hiveSeen = false;
        _decided = false;

        RestoreBuilt(planet, cell);
        PlaceQueens(ev.Grids);
    }

    private void PlaceQueens(IReadOnlyList<EntityUid> grids)
    {
        var slots = new List<EntityCoordinates>();
        var query = EntityQueryEnumerator<ST14SectorQueenSlotComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } grid && grids.Contains(grid))
                slots.Add(xform.Coordinates);
        }

        _random.Shuffle(slots);
        // the queen comes with the hive, a server without it leaves the places empty
        var settings = Settings;
        if (settings.Queen is not { } queen || !_prototype.HasIndex(queen))
            return;

        foreach (var slot in slots.Take(settings.Queens))
        {
            Spawn(queen, slot);
        }
    }

    #endregion

    #region Round end

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New == GameRunLevel.InRound)
            _nextCheck = _timing.CurTime + Settings.GracePeriod;

        // the round is over, however it ended: keep what was built for the next fight here
        if (ev.New == GameRunLevel.PostRound && _sector is { } sector && !_captured)
            SaveBuilt(sector.Planet, sector.Cell);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _sector = null;
        _grids.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_sector is not { } sector || _decided || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + Settings.CheckInterval;

        CountSides(sector.Map, out var troopers, out var hive);
        _troopersSeen |= troopers > 0;
        _hiveSeen |= hive > 0;

        // a side that never showed up cannot lose, there was nobody to fight
        if (!_troopersSeen || !_hiveSeen || troopers > 0 && hive > 0)
            return;

        _decided = true;
        var federationWon = hive == 0;
        _war.ApplyResult(sector.Planet, federationWon, false);
        _ticker.EndRound(Loc.GetString(federationWon ? "st14-sector-troopers-won" : "st14-sector-hive-won"));
    }

    /// <summary>
    /// Live fighters of both sides on the sector map: troopers are people with a player behind them, the hive is
    /// every live mob of the hive's faction, players or not: a queen keeps laying larvae while she lives
    /// </summary>
    private void CountSides(MapId map, out int troopers, out int hive)
    {
        troopers = 0;
        hive = 0;
        var faction = Settings.HiveFaction;

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != map || _mobState.IsDead(uid))
                continue;

            if (TryComp<NpcFactionMemberComponent>(uid, out var member) && _faction.IsMember((uid, member), faction))
                hive++;
            else if (HasComp<HumanoidProfileComponent>(uid) && TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
                troopers++;
        }
    }

    #endregion

    #region What stays on the map

    private sealed class SavedEntity
    {
        public string Prototype { get; set; } = string.Empty;
        public int Grid { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Rotation { get; set; }
        public bool Anchored { get; set; }
    }

    private static ResPath SavePath(string planet, int cell)
    {
        return SaveRoot / $"{planet}_{cell}.json";
    }

    /// <summary>
    /// Writes down everything on the sector map that outlives the round, by grid and position on it
    /// </summary>
    private void SaveBuilt(string planet, int cell)
    {
        var grids = _grids;
        var saved = new List<SavedEntity>();

        var query = EntityQueryEnumerator<ST14SectorPersistentComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var meta, out var xform))
        {
            if (meta.EntityPrototype is not { } proto || xform.GridUid is not { } grid || grids.IndexOf(grid) is var index && index < 0)
                continue;

            saved.Add(new SavedEntity
            {
                Prototype = proto.ID,
                Grid = index,
                X = xform.LocalPosition.X,
                Y = xform.LocalPosition.Y,
                Rotation = (float) xform.LocalRotation.Theta,
                Anchored = xform.Anchored,
            });
        }

        try
        {
            _resources.UserData.CreateDir(SaveRoot);
            using var writer = _resources.UserData.OpenWriteText(SavePath(planet, cell));
            writer.Write(JsonSerializer.Serialize(saved));
            Log.Info($"Saved {saved.Count} entities of sector {cell} of {planet}");
        }
        catch (Exception e)
        {
            Log.Error($"Could not save sector {cell} of {planet}: {e}");
        }
    }

    /// <summary>
    /// Puts back what stayed on the map from the last round in this sector, in place of what the mapper laid out
    /// </summary>
    private void RestoreBuilt(string planet, int cell)
    {
        var path = SavePath(planet, cell);
        if (!_resources.UserData.Exists(path))
            return;

        List<SavedEntity>? saved;
        try
        {
            using var reader = _resources.UserData.OpenText(path);
            saved = JsonSerializer.Deserialize<List<SavedEntity>>(reader.ReadToEnd());
        }
        catch (Exception e)
        {
            Log.Error($"Could not read the save of sector {cell} of {planet}: {e}");
            return;
        }

        if (saved == null)
            return;

        var grids = _grids;

        // the save is the whole truth about these things: what the map brought along goes first
        var mapped = new List<EntityUid>();
        var query = EntityQueryEnumerator<ST14SectorPersistentComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && grids.Contains(grid))
                mapped.Add(uid);
        }

        foreach (var uid in mapped)
        {
            QueueDel(uid);
        }

        foreach (var entity in saved)
        {
            if (entity.Grid < 0 || entity.Grid >= grids.Count || !_prototype.HasIndex<EntityPrototype>(entity.Prototype))
                continue;

            var uid = Spawn(entity.Prototype, new EntityCoordinates(grids[entity.Grid], new Vector2(entity.X, entity.Y)));
            _transform.SetLocalRotation(uid, new Angle(entity.Rotation));
            if (entity.Anchored)
                _transform.AnchorEntity(uid);
        }

        Log.Info($"Restored {saved.Count} entities of sector {cell} of {planet}");
    }

    /// <summary>
    /// A sector taken by one side starts clean: its save goes
    /// </summary>
    private void OnSectorResult(ref ST14SectorResultEvent ev)
    {
        if (ev.Liberation > 0 && ev.Liberation < 100)
            return;

        var path = SavePath(ev.Planet, ev.Cell);
        if (_resources.UserData.Exists(path))
            _resources.UserData.Delete(path);

        // the round that took it must not write it down again
        if (_sector is { } sector && sector.Planet == ev.Planet && sector.Cell == ev.Cell)
            _captured = true;
    }

    #endregion
}
