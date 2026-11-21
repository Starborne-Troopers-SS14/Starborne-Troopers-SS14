using Content.Shared._ST14.CCVar;
using Robust.Shared.Configuration;
using Content.Shared._ST14.Weather;
using Robust.Shared.Map.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Weather;

/// <summary>
/// Shows the weather of the sector the player is in: the overlay with its particles and clouds, and its one looping
/// sound. Both follow the strength of the event, which builds up slowly and dies down at the end. A new event takes
/// over with a cross-fade, so sounds never pile up
/// </summary>
public sealed partial class ST14SectorWeatherClientSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private Content.Shared.Light.EntitySystems.SharedRoofSystem _roof = default!; // ST14
    [Dependency] private SharedMapSystem _map = default!; // ST14
    [Dependency] private SharedTransformSystem _transform = default!; // ST14
    [Dependency] private Content.Client._ST14.ZLevels.Core.ST14ClientZLevelsSystem _zLevels = default!; // ST14
    [Dependency] private Content.Client._ST14.ZLevels.Roof.ST14ClientZLevelsRoofSystem _zRoofs = default!; // ST14

    /// <summary>
    /// ST14: 1 under the open sky, 0 under a roof (the ground over a tunnel, a roof built on the level above),
    /// eased so stepping under a roof fades the weather out instead of cutting it
    /// </summary>
    private float _exposure = 1f;

    /// <summary>
    /// Light sand over every sector with a quiet sky (its colour and pace come from this prototype), and how strong an
    /// event has to be to hide it entirely. It is a layer of its own under the weather (a shader), so an event never
    /// has to give way to it
    /// </summary>
    private static readonly ProtoId<ST14WeatherEventPrototype> LightSand = "ST14WeatherLightSand";
    private const float LightSandHiddenAt = 0.5f;

    /// <summary>
    /// The light sand is off for now: the shader stays, nothing is drawn until this is turned back on
    /// </summary>
    private const bool LightSandOn = false;

    private ST14WeatherOverlay _overlay = default!;

    private string? _playing;
    private EntityUid? _stream;
    private float _gain;
    private readonly List<(EntityUid Stream, float Gain)> _fading = new();

    /// <summary>
    /// Weather slider of the audio options
    /// </summary>
    private float _volume = 1f;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new ST14WeatherOverlay(_resources);
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<ST14WeatherFlashEvent>(OnFlash);
        Subs.CVar(_cfg, ST14CVars.WeatherVolume, value => _volume = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay(_overlay);
        StopAll();
    }

    private void OnFlash(ST14WeatherFlashEvent ev)
    {
        if (_player.LocalEntity is { } ent && Transform(ent).MapUid == GetEntity(ev.Map))
            _overlay.Flash(ev.Position);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        ST14WeatherEventPrototype? proto = null;
        var strength = 0f;
        var wind = new System.Numerics.Vector2(1, 0);
        var brightness = 1f;
        var sector = false;

        if (_player.LocalEntity is { } ent && Transform(ent).MapUid is { } map)
        {
            if (TryComp<ST14SectorWeatherComponent>(map, out var sky))
            {
                // every sector map has a sky, with or without an event in it
                sector = true;
                wind = sky.Wind;
                if (sky.Event is { } id && _prototype.TryIndex(id, out proto))
                    strength = proto.Strength(_timing.CurTime, sky.Start, sky.End);
            }

            // the weather is lit by the same sky as the ground under it
            if (TryComp<MapLightComponent>(map, out var light))
            {
                var c = light.AmbientLightColor;
                brightness = Math.Clamp(MathF.Max(c.R, MathF.Max(c.G, c.B)) * 1.1f, 0.14f, 1f);
            }
        }

        // ST14: the same weather is on every z-level of a sector, but only what is under the open sky sees it
        // under any roof (a tunnel, a bunker, a dug-out) none of it; down in an open trench a little less of it
        var exposed = _player.LocalEntity is not { } local ? 1f : Exposure(local);
        _exposure += (exposed - _exposure) * MathF.Min(1f, frameTime * 3f);
        strength *= _exposure;

        // the event as it is, an ended one dies down by itself
        _overlay.Set(proto?.ID, proto?.Visual, strength, wind, brightness, frameTime);
        // the light sand is off for now (the user asked): the shader stays, nothing is drawn
        SetLightSand(sector && LightSandOn, frameTime);
        UpdateSound(proto, strength, frameTime);
    }

    /// <summary>
    /// The light sand that always drifts over a sector: only under the open sky, and fading as an event (or the one
    /// still dying down) takes the sky over
    /// </summary>
    private void SetLightSand(bool sector, float frameTime)
    {
        if (!sector || !_prototype.TryIndex(LightSand, out var sand))
        {
            _overlay.SetLightSand(0f, Color.Transparent, 0f, frameTime);
            return;
        }

        var weather = _overlay.Event == null ? 0f : _overlay.Strength;
        var left = Math.Clamp(1f - weather / LightSandHiddenAt, 0f, 1f);
        _overlay.SetLightSand(_exposure * left, sand.Visual.Color, sand.Visual.Speed, frameTime);
    }

    private void UpdateSound(ST14WeatherEventPrototype? proto, float strength, float frameTime)
    {
        var wanted = proto?.Ambient != null ? proto.ID : null;
        if (wanted != _playing)
        {
            if (_stream is { } old)
                _fading.Add((old, _gain));

            _stream = null;
            _gain = 0f;
            _playing = wanted;

            if (proto?.Ambient is { } sound)
            {
                var looped = sound.Params.WithLoop(true);
                _stream = _audio.PlayGlobal(sound, Filter.Local(), false, looped)?.Entity;
                if (_stream is { } fresh)
                    _audio.SetGain(fresh, 0f);
            }
        }

        if (_stream is { } stream)
        {
            if (TerminatingOrDeleted(stream))
            {
                _stream = null;
                _playing = null;
            }
            else
            {
                // the volume eases after the strength, it never jumps
                _gain += (strength - _gain) * MathF.Min(1f, frameTime * 1.5f);
                _audio.SetGain(stream, _gain * _volume);
            }
        }

        for (var i = _fading.Count - 1; i >= 0; i--)
        {
            var (old, gain) = _fading[i];
            gain -= frameTime / 3f;
            if (gain <= 0f || TerminatingOrDeleted(old))
            {
                _audio.Stop(old);
                _fading.RemoveAt(i);
                continue;
            }

            _audio.SetGain(old, gain * _volume);
            _fading[i] = (old, gain);
        }
    }

    /// <summary>
    /// The weather loops this system fades itself, the audio categories system leaves them alone
    /// </summary>
    public bool OwnsStream(EntityUid uid)
    {
        if (_stream == uid)
            return true;

        foreach (var (old, _) in _fading)
        {
            if (old == uid)
                return true;
        }

        return false;
    }

    private void StopAll()
    {
        if (_stream is { } stream)
            _audio.Stop(stream);

        foreach (var (old, _) in _fading)
        {
            _audio.Stop(old);
        }

        _fading.Clear();
        _stream = null;
        _playing = null;
    }

    /// <summary>
    /// ST14: how much of the weather reaches down into an open trench
    /// </summary>
    private const float TrenchExposure = 0.6f;

    /// <summary>
    /// ST14: how much of it is seen from inside a dug-out or a bunker: a light rain outside, to show it is coming down
    /// </summary>
    private const float DugoutExposure = 0.25f;

    /// <summary>
    /// ST14: how much of the weather the entity sees where it stands: all of it under the open sky, less down in an open
    /// trench, a little in a dug-out or a bunker, none under solid ground (a tunnel)
    /// </summary>
    private float Exposure(EntityUid uid)
    {
        var xform = Transform(uid);

        // a dug-out or a bunker roof over the tile (it roofs the tile the vanilla way too, so it is checked first)
        var world = _transform.GetWorldPosition(xform);
        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        if (xform.MapUid is { } map && _zLevels.TryMapUp(map, out var above) &&
            _zRoofs.TryGetRoofEntity(above.Owner, tile, out _))
        {
            return DugoutExposure;
        }

        if (UnderRoof(uid))
            return 0f;

        return InTrench(uid) ? TrenchExposure : 1f;
    }

    /// <summary>
    /// ST14: whether the tile the entity stands on is rooved, i.e. something opaque lies over it on a z-level above
    /// </summary>
    private bool UnderRoof(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !TryComp<Content.Shared.Light.Components.RoofComponent>(gridUid, out var roof))
        {
            return false;
        }

        var index = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        return _roof.IsRooved((gridUid, grid, roof), index);
    }

    /// <summary>
    /// ST14: the entity is below the ground, in a trench open to the sky
    /// </summary>
    private bool InTrench(EntityUid uid)
    {
        return Transform(uid).MapUid is { } map &&
               TryComp<Content.Shared._ST14.ZLevels.Core.Components.ST14ZMapComponent>(map, out var zMap) && zMap.Depth < 0;
    }
}
