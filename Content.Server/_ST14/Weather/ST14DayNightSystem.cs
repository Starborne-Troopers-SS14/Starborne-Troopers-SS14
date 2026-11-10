using System.Numerics;
using Content.Server._ST14.MainHub;
using Content.Server._ST14.Sectors;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.Weather;
using Content.Shared.Light.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Weather;

/// <summary>
/// Day and night on the sectors of a round. Every sector is a hex of the planet: the star lights the half of the
/// planet facing it, the planet turns, and the light of the sky of each sector map follows the height of the star over
/// that hex: night, dawn, day, dusk
/// </summary>
public sealed partial class ST14DayNightSystem : EntitySystem
{
    private static readonly ProtoId<ST14WeatherSettingsPrototype> SettingsId = "Default";

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;

    private TimeSpan _nextUpdate;

    /// <summary>
    /// Maps whose sky flashes white until the time, a lightning strike
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _flashes = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var flashEnded = false;
        foreach (var (map, until) in _flashes)
        {
            if (now < until)
                continue;

            _flashes.Remove(map);
            flashEnded = true;
            break;
        }

        if (!flashEnded && now < _nextUpdate)
            return;

        _nextUpdate = now + _prototype.Index(SettingsId).LightInterval;
        UpdateLight();
    }

    /// <summary>
    /// The sky of a sector flashes white for a moment
    /// </summary>
    public void Flash(EntityUid map, TimeSpan length)
    {
        _flashes[map] = _timing.CurTime + length;
        if (TryComp<MapLightComponent>(map, out var light))
        {
            light.AmbientLightColor = Color.FromHex("#F4F6FF");
            Dirty(map, light);
        }
    }

    /// <summary>
    /// The direction to the star in the frame of the planet right now
    /// </summary>
    public Vector3 Star()
    {
        return ST14PlanetDay.StarDirection(ST14PlanetDay.Phase(DateTime.UtcNow,
            _cfg.GetCVar(ST14CVars.DayLength),
            _cfg.GetCVar(ST14CVars.DayPhaseOffset)));
    }

    /// <summary>
    /// Where on the planet the sector of a map lies, false for a map that is not a sector
    /// </summary>
    public bool TryGetPoint(EntityUid map, out Vector3 point)
    {
        point = default;
        if (!TryComp<ST14SectorMapComponent>(map, out var sector) ||
            !_war.TryGetWar(sector.Planet, out var war, out _) ||
            sector.Cell < 0 || sector.Cell >= war.Grid.Count)
        {
            return false;
        }

        point = war.Grid.Centers[sector.Cell];
        return true;
    }

    /// <summary>
    /// Sine of the height of the star over a sector map, 1 for a map that is not a sector
    /// </summary>
    public float StarHeight(EntityUid map)
    {
        return TryGetPoint(map, out var point) ? ST14PlanetDay.StarHeight(point, Star()) : 1f;
    }

    /// <summary>
    /// Moves the shift of the turn so that the sector of the map has the local hour, for admins and for tests
    /// </summary>
    public bool SetHour(EntityUid map, float hour)
    {
        if (!TryGetPoint(map, out var point))
            return false;

        var wanted = ST14PlanetDay.PhaseForHour(point, hour);
        var running = ST14PlanetDay.Phase(DateTime.UtcNow, _cfg.GetCVar(ST14CVars.DayLength), 0f);
        var offset = wanted - running;
        _cfg.SetCVar(ST14CVars.DayPhaseOffset, offset - MathF.Floor(offset));
        _nextUpdate = TimeSpan.Zero;
        return true;
    }

    private void UpdateLight()
    {
        var star = Star();
        foreach (var map in _sectors.LoadedMaps.Values)
        {
            if (TerminatingOrDeleted(map) || _flashes.ContainsKey(map) || !TryGetPoint(map, out var point))
                continue;

            var light = EnsureComp<MapLightComponent>(map);
            var color = ST14PlanetDay.SkyLight(ST14PlanetDay.StarHeight(point, star));
            if (!Close(light.AmbientLightColor, color))
            {
                light.AmbientLightColor = color;
                Dirty(map, light);
            }

            OpenSky(map);
        }
    }

    /// <summary>
    /// A sector is open ground under the sky. Grids get an implicit black roof when they load, which keeps the light of
    /// the sky off every tile, so it goes
    /// </summary>
    private void OpenSky(EntityUid map)
    {
        var mapId = Transform(map).MapID;
        var grids = EntityQueryEnumerator<ImplicitRoofComponent, MapGridComponent, TransformComponent>();
        var open = new List<EntityUid>();
        while (grids.MoveNext(out var grid, out _, out _, out var xform))
        {
            if (xform.MapID == mapId)
                open.Add(grid);
        }

        foreach (var grid in open)
        {
            RemComp<ImplicitRoofComponent>(grid);
        }
    }

    private static bool Close(Color a, Color b)
    {
        return MathF.Abs(a.R - b.R) < 0.002f && MathF.Abs(a.G - b.G) < 0.002f && MathF.Abs(a.B - b.B) < 0.002f;
    }
}
