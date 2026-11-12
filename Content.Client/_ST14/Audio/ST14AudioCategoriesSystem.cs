using Content.Client._ST14.Weather;
using Content.Shared._ST14.CCVar;
using Robust.Client.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;

namespace Content.Client._ST14.Audio;

/// <summary>
/// Per-category volume sliders: gunshots, explosions, weather, voices, bugs, footsteps, machines.
/// The category of a sound is told by the folder of its file, so sounds the server plays are covered too.
/// The engine reads the volume of a sound from its params every frame, so the system scales the params
/// before the engine plays them and keeps the original volume to scale it again when the slider moves.
/// </summary>
public sealed partial class ST14AudioCategoriesSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ST14SectorWeatherClientSystem _weather = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private sealed class Category(CVarDef<float> cvar, string[] prefixes, string[]? exclude = null)
    {
        public readonly CVarDef<float> CVar = cvar;
        public readonly string[] Prefixes = prefixes;
        public readonly string[] Exclude = exclude ?? [];
        public float Offset;
    }

    private readonly Category[] _categories =
    [
        new(ST14CVars.WeaponsVolume, ["/Audio/Weapons/", "/Audio/_ST14/SoundCollections/Weapons/", "/Audio/_ST14/Weapons/Impacts/"]),
        new(ST14CVars.ExplosionsVolume, ["/Audio/Effects/explosion", "/Audio/Effects/Explosion", "/Audio/_ST14/Weapons/Explosions/"]),
        new(ST14CVars.WeatherVolume, ["/Audio/_ST14/Weather/"]),
        new(ST14CVars.VoicesVolume, ["/Audio/_ST14/Voice/"]),
        // bug footsteps are played and scaled by their own system and slider
        new(ST14CVars.BugsVolume, ["/Audio/_ST14/Mobs/Bugs/"], ["/Audio/_ST14/Mobs/Bugs/Steps/"]),
        new(ST14CVars.FootstepsVolume, ["/Audio/Effects/Footsteps/", "/Audio/_ST14/Effects/Footsteps/"]),
        new(ST14CVars.MachinesVolume, ["/Audio/_ST14/ST14Resources/", "/Audio/Machines/"]),
    ];

    /// <summary>
    /// Sounds already sorted: category (-1 for none), the volume they came with and the volume set here.
    /// </summary>
    private readonly Dictionary<EntityUid, (int Category, float Base, float Applied)> _tracked = new();
    private readonly List<EntityUid> _gone = new();

    public override void Initialize()
    {
        base.Initialize();

        // scale before the engine reads the params for this frame
        UpdatesBefore.Add(typeof(AudioSystem));

        foreach (var category in _categories)
        {
            var cat = category;
            Subs.CVar(_cfg, cat.CVar, value => cat.Offset = SharedAudioSystem.GainToVolume(MathF.Max(value, 0.0001f)), true);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _gone.Clear();
        foreach (var uid in _tracked.Keys)
        {
            if (!Exists(uid))
                _gone.Add(uid);
        }

        foreach (var uid in _gone)
        {
            _tracked.Remove(uid);
        }

        var query = EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            if (!_tracked.TryGetValue(uid, out var entry))
            {
                var index = FindCategory(audio.FileName);
                entry = (index, audio.Params.Volume, float.NaN);
            }

            // the weather loops are faded by the weather system, it applies the weather slider itself
            if (entry.Category < 0 || _weather.OwnsStream(uid))
            {
                _tracked[uid] = (-1, 0f, 0f);
                continue;
            }

            // someone else (the server, a fade) changed the volume: that is the new original
            if (!float.IsNaN(entry.Applied) && audio.Params.Volume != entry.Applied)
                entry.Base = audio.Params.Volume;

            var wanted = entry.Base + _categories[entry.Category].Offset;
            if (audio.Params.Volume != wanted)
                _audio.SetVolume(uid, wanted, audio);

            _tracked[uid] = (entry.Category, entry.Base, wanted);
        }
    }

    private int FindCategory(string? file)
    {
        if (string.IsNullOrEmpty(file))
            return -1;

        for (var i = 0; i < _categories.Length; i++)
        {
            var category = _categories[i];
            var excluded = false;
            foreach (var prefix in category.Exclude)
            {
                if (file.StartsWith(prefix, StringComparison.Ordinal))
                    excluded = true;
            }

            if (excluded)
                continue;

            foreach (var prefix in category.Prefixes)
            {
                if (file.StartsWith(prefix, StringComparison.Ordinal))
                    return i;
            }
        }

        return -1;
    }
}
