using System.Linq;
using System.Numerics;
using Content.Server._ST14.Sectors;
using Content.Shared.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.Weather;
using Content.Shared.Camera;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weather;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Weather;

/// <summary>
/// Weather and natural disasters on the sectors of a round. Every sector map has weather of its own: after a quiet
/// while an event picked by weight starts there, lasts a few minutes and does what its effects say
/// </summary>
public sealed partial class ST14SectorWeatherSystem : EntitySystem
{
    private static readonly ProtoId<ST14WeatherSettingsPrototype> SettingsId = "Default";
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(0.25);

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private ST14DayNightSystem _dayNight = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;
    [Dependency] private ST14WeatherSlowSystem _slow = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SharedWeatherSystem _weather = default!;

    private sealed class MapWeather
    {
        public TimeSpan Next;
        public ActiveEvent? Active;
    }

    private sealed class ActiveEvent
    {
        public ST14WeatherEventPrototype Proto = default!;
        public TimeSpan Ends;
        public TimeSpan[] NextEffect = Array.Empty<TimeSpan>();
        public TimeSpan Started;
        public Vector2 Wind;

        /// <summary>
        /// Strength right now, 0 to 1, see <see cref="ST14WeatherEventPrototype.Strength"/>
        /// </summary>
        public float Strength;
    }

    private readonly Dictionary<EntityUid, MapWeather> _maps = new();
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();

        // the weather goes before the sectors are written to their saves
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged, before: new[] { typeof(ST14SectorSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private ST14WeatherSettingsPrototype Settings => _prototype.Index(SettingsId);

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New == GameRunLevel.PostRound)
            StopAll();
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _maps.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        UpdateStrikes();

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + Tick;
        var settings = Settings;
        var enabled = _cfg.GetCVar(ST14CVars.SectorWeather);

        foreach (var map in _sectors.LoadedMaps.Values)
        {
            if (TerminatingOrDeleted(map))
                continue;

            if (!_maps.TryGetValue(map, out var weather))
            {
                weather = new MapWeather { Next = now + settings.FirstDelay + Gap(settings) * _random.NextFloat() };
                _maps[map] = weather;
            }

            if (weather.Active is { } active)
            {
                if (now >= active.Ends)
                    Stop(map, weather, false);
                else
                    Run(map, active, now);

                continue;
            }

            if (!enabled || now < weather.Next)
                continue;

            if (Pick(map) is { } proto)
                Start(map, proto);
            else
                weather.Next = now + Gap(settings);
        }
    }

    private TimeSpan Gap(ST14WeatherSettingsPrototype settings)
    {
        return settings.MinGap + (settings.MaxGap - settings.MinGap) * _random.NextDouble();
    }

    /// <summary>
    /// An event that fits the time of day of the sector, by weight
    /// </summary>
    private ST14WeatherEventPrototype? Pick(EntityUid map)
    {
        var day = _dayNight.StarHeight(map) > 0;
        var events = _prototype.EnumeratePrototypes<ST14WeatherEventPrototype>()
            .Where(e => e.Weight > 0 && (e.Time == ST14WeatherTime.Any || e.Time == ST14WeatherTime.Day == day))
            .ToList();

        var total = events.Sum(e => e.Weight);
        var roll = _random.NextFloat() * total;
        foreach (var e in events)
        {
            roll -= e.Weight;
            if (roll <= 0)
                return e;
        }

        return events.LastOrDefault();
    }

    #region Start and stop

    /// <summary>
    /// Starts an event on a sector map, ending the one going on there
    /// </summary>
    public bool Start(EntityUid map, ST14WeatherEventPrototype proto, TimeSpan? duration = null, bool full = false)
    {
        if (!_maps.TryGetValue(map, out var weather))
        {
            weather = new MapWeather();
            _maps[map] = weather;
        }

        if (weather.Active != null)
            Stop(map, weather, true);

        var now = _timing.CurTime;
        var length = duration ?? proto.MinDuration + (proto.MaxDuration - proto.MinDuration) * _random.NextDouble();
        var active = new ActiveEvent
        {
            Proto = proto,
            // an admin can skip the build-up
            Started = full ? now - proto.RampUp : now,
            Ends = now + length,
            NextEffect = new TimeSpan[proto.Effects.Count],
            Wind = new Angle(_random.NextDouble() * Math.Tau).ToVec(),
        };

        // the first blow comes a moment after the warning
        for (var i = 0; i < proto.Effects.Count; i++)
        {
            active.NextEffect[i] = now + proto.Effects[i].Interval * _random.NextDouble();
        }

        weather.Active = active;

        var sky = EnsureComp<ST14SectorWeatherComponent>(map);
        sky.Event = proto.ID;
        sky.Start = active.Started;
        sky.End = active.Ends;
        sky.Wind = active.Wind;
        Dirty(map, sky);

        if (proto.StartSound != null)
            _audio.PlayGlobal(proto.StartSound, MapFilter(map), true);

        Tell(map, Loc.GetString(proto.Warning), PopupType.LargeCaution, Color.FromHex("#E8A040"));
        Log.Info($"Weather {proto.ID} starts on {ToPrettyString(map)} for {length.TotalSeconds:0} s");
        return true;
    }

    private void Stop(EntityUid map, MapWeather weather, bool now)
    {
        if (weather.Active is not { } active)
            return;

        weather.Active = null;
        weather.Next = _timing.CurTime + Gap(Settings);

        // a new event takes the sky over itself, the clients cross over from one to the other
        if (!now)
            RemComp<ST14SectorWeatherComponent>(map);

        Slow(map, 1f);

        if (!now && active.Proto.Over is { } over)
            Tell(map, Loc.GetString(over), PopupType.Medium, Color.FromHex("#A8C8A0"));
    }

    /// <summary>
    /// Ends the event on a map at once, false if there was none
    /// </summary>
    public bool Stop(EntityUid map)
    {
        if (!_maps.TryGetValue(map, out var weather) || weather.Active is not { } active)
            return false;

        // it dies down quickly rather than vanishing
        var now = _timing.CurTime;
        var fade = active.Proto.RampDown;
        if (active.Ends > now + fade)
        {
            active.Ends = now + fade;
            if (TryComp<ST14SectorWeatherComponent>(map, out var sky))
            {
                // the clients fade it out over the same seconds
                sky.End = active.Ends;
                Dirty(map, sky);
            }
        }

        return true;
    }

    public ST14WeatherEventPrototype? Current(EntityUid map)
    {
        return _maps.TryGetValue(map, out var weather) ? weather.Active?.Proto : null;
    }

    /// <summary>
    /// The round is over: everything the weather put on the maps goes at once, before the sectors are saved
    /// </summary>
    private void StopAll()
    {
        foreach (var (map, weather) in _maps)
        {
            if (weather.Active is not { } active)
                continue;

            weather.Active = null;
            if (TerminatingOrDeleted(map))
                continue;

            RemComp<ST14SectorWeatherComponent>(map);
            Slow(map, 1f);
        }

        var deleted = new List<EntityUid>();
        var strikes = EntityQueryEnumerator<ST14WeatherStrikeComponent>();
        while (strikes.MoveNext(out var uid, out _))
        {
            deleted.Add(uid);
        }

        foreach (var uid in deleted)
        {
            Del(uid);
        }
    }

    #endregion

    #region Effects

    private void Run(EntityUid map, ActiveEvent active, TimeSpan now)
    {
        active.Strength = active.Proto.Strength(now, active.Started, active.Ends);
        var strength = active.Strength;
        Slow(map, 1f + (active.Proto.Speed - 1f) * strength);

        // the first minutes of an event are only a warning of what is coming
        if (strength < 0.15f)
            return;

        for (var i = 0; i < active.Proto.Effects.Count; i++)
        {
            if (now < active.NextEffect[i])
                continue;

            var effect = active.Proto.Effects[i];
            active.NextEffect[i] = now + effect.Interval;
            // the weaker the event, the rarer its blows
            if (!_random.Prob(Math.Clamp(effect.Chance * strength, 0f, 1f)))
                continue;

            switch (effect)
            {
                case ST14WeatherShakeEffect shake:
                    Shake(map, shake, strength);
                    break;
                case ST14WeatherStrikeEffect strike:
                    for (var n = 0; n < Math.Max(1, (int) MathF.Round(strike.Count * strength)); n++)
                    {
                        SpawnStrike(map, strike);
                    }
                    break;
                case ST14WeatherGustEffect gust:
                    Gust(map, gust, active.Wind);
                    break;
                case ST14WeatherExposureEffect exposure:
                    if (strength > 0.4f)
                        Expose(map, exposure);
                    break;
                case ST14WeatherExtinguishEffect:
                    Extinguish(map);
                    break;
                case ST14WeatherSoundEffect sound:
                    if (RandomSpot(map, sound.Spread, 0f) is { } place)
                        _audio.PlayPvs(sound.Sound, place);
                    break;
            }
        }
    }

    private void Shake(EntityUid map, ST14WeatherShakeEffect shake, float strength)
    {
        foreach (var (uid, outside) in Mobs(map))
        {
            if (HasComp<CameraRecoilComponent>(uid))
                _recoil.KickCamera(uid, _random.NextVector2(0.3f, 1f) * shake.Strength * strength);

            if (shake.KnockdownChance > 0 && _random.Prob(shake.KnockdownChance * strength) && !_mobState.IsIncapacitated(uid))
            {
                if (_stun.TryKnockdown(uid, shake.KnockdownTime))
                    _popup.PopupEntity(Loc.GetString("st14-weather-quake-fall"), uid, uid, PopupType.SmallCaution);
            }
        }
    }

    private void Gust(EntityUid map, ST14WeatherGustEffect gust, Vector2 wind)
    {
        // the wind veers a little with every gust
        var dir = new Angle(_random.NextFloat(-0.35f, 0.35f)).RotateVec(wind);
        var mapId = Transform(map).MapID;

        if (gust.Sound != null)
            _audio.PlayGlobal(gust.Sound, MapFilter(map), true);

        var items = new HashSet<EntityUid>();
        foreach (var (uid, outside) in Mobs(map))
        {
            if (!outside)
                continue;

            if (HasComp<CameraRecoilComponent>(uid))
                _recoil.KickCamera(uid, dir * 0.4f);

            if (_random.Prob(gust.PushChance) && !_container.IsEntityInContainer(uid))
            {
                _throwing.TryThrow(uid, dir * 1.3f, 4f, recoil: false, playSound: false, doSpin: false);
                if (_random.Prob(gust.KnockdownChance) && _stun.TryKnockdown(uid, TimeSpan.FromSeconds(1.5)))
                    _popup.PopupEntity(Loc.GetString("st14-weather-gust-fall"), uid, uid, PopupType.SmallCaution);
            }

            if (!HasComp<ActorComponent>(uid))
                continue;

            foreach (var item in _lookup.GetEntitiesInRange<ItemComponent>(Transform(uid).Coordinates, 10f, LookupFlags.Dynamic | LookupFlags.Sundries))
            {
                items.Add(item.Owner);
            }
        }

        foreach (var item in items)
        {
            if (!_random.Prob(gust.ItemChance) || _container.IsEntityInContainer(item) || Transform(item).MapID != mapId)
                continue;

            _throwing.TryThrow(item, dir * gust.Distance * _random.NextFloat(0.5f, 1f), 7f, recoil: false, playSound: false);
        }
    }

    private void Expose(EntityUid map, ST14WeatherExposureEffect exposure)
    {
        foreach (var (uid, outside) in Mobs(map))
        {
            if (!outside || _mobState.IsDead(uid))
                continue;

            _damage.TryChangeDamage(uid, exposure.Damage, interruptsDoAfters: false);
            if (exposure.Popup is { } popup && _random.Prob(0.3f))
                _popup.PopupEntity(Loc.GetString(popup), uid, uid, PopupType.SmallCaution);
        }
    }

    private void Extinguish(EntityUid map)
    {
        foreach (var (uid, outside) in Mobs(map))
        {
            if (outside && TryComp<FlammableComponent>(uid, out var flammable) && flammable.OnFire)
                _flammable.Extinguish(uid, flammable);
        }
    }

    private void Slow(EntityUid map, float speed)
    {
        foreach (var (uid, outside) in Mobs(map))
        {
            _slow.SetSpeed(uid, outside ? speed : 1f);
        }
    }

    #endregion

    #region Places

    /// <summary>
    /// The living and the dying on a map, and whether each stands under the open sky
    /// </summary>
    private List<(EntityUid Uid, bool Outside)> Mobs(EntityUid map)
    {
        var mapId = Transform(map).MapID;
        var mobs = new List<(EntityUid, bool)>();
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId || _mobState.IsDead(uid))
                continue;

            mobs.Add((uid, Outside(xform)));
        }

        return mobs;
    }

    private bool Outside(TransformComponent xform)
    {
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return true;

        // inside a locker or a vehicle, out of the weather
        if (xform.ParentUid != grid)
            return false;

        var tile = _map.GetTileRef(grid, gridComp, xform.Coordinates);
        return _weather.CanWeatherAffect((grid, gridComp, null), tile);
    }

    private Filter MapFilter(EntityUid map)
    {
        return Filter.BroadcastMap(Transform(map).MapID);
    }

    /// <summary>
    /// A place on the ground of the map: near a player, or anywhere with the chance <paramref name="anywhere"/>
    /// </summary>
    private EntityCoordinates? RandomSpot(EntityUid map, float spread, float anywhere)
    {
        var mapId = Transform(map).MapID;
        var players = new List<EntityUid>();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId && xform.GridUid != null)
                players.Add(uid);
        }

        var grids = new List<Entity<MapGridComponent>>();
        var gridQuery = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID == mapId)
                grids.Add((uid, grid));
        }

        if (grids.Count == 0)
            return null;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            Vector2 world;
            if (players.Count > 0 && !_random.Prob(anywhere))
            {
                world = _transform.GetWorldPosition(_random.Pick(players)) + _random.NextVector2(spread);
            }
            else
            {
                var pick = _random.Pick(grids);
                var box = _transform.GetWorldMatrix(pick.Owner);
                var local = pick.Comp.LocalAABB;
                world = Vector2.Transform(new Vector2(
                    _random.NextFloat(local.Left, local.Right),
                    _random.NextFloat(local.Bottom, local.Top)), box);
            }

            var coords = new MapCoordinates(world, mapId);
            foreach (var grid in grids)
            {
                var index = _map.WorldToTile(grid.Owner, grid.Comp, world);
                if (!_map.TryGetTileRef(grid.Owner, grid.Comp, index, out var tile) || tile.Tile.IsEmpty)
                    continue;

                // the sandstorm on the edges of the map swallows whatever falls into it
                if (!_weather.CanWeatherAffect((grid.Owner, grid.Comp, null), tile))
                    continue;

                return _transform.ToCoordinates(grid.Owner, coords);
            }
        }

        return null;
    }

    private void SpawnStrike(EntityUid map, ST14WeatherStrikeEffect strike)
    {
        if (RandomSpot(map, strike.Spread, strike.Anywhere) is { } spot)
            Spawn(strike.Strike, spot);
    }

    #endregion

    /// <summary>
    /// Tells everyone on the map: a popup over them and a line in the chat
    /// </summary>
    private void Tell(EntityUid map, string message, PopupType popup, Color color)
    {
        var mapId = Transform(map).MapID;
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            _popup.PopupEntity(message, uid, uid, popup);
            var wrapped = Loc.GetString("st14-weather-chat", ("message", message));
            _chat.ChatMessageToOne(ChatChannel.Server, message, wrapped, default, false, actor.PlayerSession.Channel, color);
        }
    }
}
