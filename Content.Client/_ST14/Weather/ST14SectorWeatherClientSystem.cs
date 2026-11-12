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

    private ST14WeatherOverlay _overlay = default!;

    private string? _playing;
    private EntityUid? _stream;
    private float _gain;
    private readonly List<(EntityUid Stream, float Gain)> _fading = new();

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new ST14WeatherOverlay(_resources);
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<ST14WeatherFlashEvent>(OnFlash);
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

        if (_player.LocalEntity is { } ent && Transform(ent).MapUid is { } map)
        {
            if (TryComp<ST14SectorWeatherComponent>(map, out var sky) && sky.Event is { } id &&
                _prototype.TryIndex(id, out proto))
            {
                strength = proto.Strength(_timing.CurTime, sky.Start, sky.End);
                wind = sky.Wind;
            }

            // the weather is lit by the same sky as the ground under it
            if (TryComp<MapLightComponent>(map, out var light))
            {
                var c = light.AmbientLightColor;
                brightness = Math.Clamp(MathF.Max(c.R, MathF.Max(c.G, c.B)) * 1.1f, 0.14f, 1f);
            }
        }

        _overlay.Set(proto?.ID, proto?.Visual, strength, wind, brightness, frameTime);
        UpdateSound(proto, strength, frameTime);
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
                _audio.SetGain(stream, _gain);
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

            _audio.SetGain(old, gain);
            _fading[i] = (old, gain);
        }
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
}
