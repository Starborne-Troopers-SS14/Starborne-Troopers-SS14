using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14MultitoolSoundSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    private readonly Dictionary<EntityUid, (ST14MultitoolMode Mode, EntityUid Target, EntityUid Stream)> _playing = new();
    private readonly List<EntityUid> _stale = new();

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var playing in _playing.Values)
        {
            _audio.Stop(playing.Stream);
        }

        _playing.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _stale.Clear();
        foreach (var (uid, playing) in _playing)
        {
            if (!TryComp<ST14MultitoolComponent>(uid, out var tool) || tool.Mode != playing.Mode || tool.Target != playing.Target)
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _audio.Stop(_playing[uid].Stream);
            _playing.Remove(uid);
        }

        var query = EntityQueryEnumerator<ST14MultitoolComponent>();
        while (query.MoveNext(out var uid, out var tool))
        {
            if (tool.Mode == ST14MultitoolMode.None || tool.Target is not { } target || _playing.ContainsKey(uid))
                continue;

            var sound = tool.Mode == ST14MultitoolMode.Dismantle ? tool.DismantleSound : tool.BuildSound;
            var audioParams = (sound?.Params ?? AudioParams.Default).WithLoop(true);

            if (_audio.PlayEntity(sound, Filter.Local(), target, false, audioParams) is { } stream)
                _playing[uid] = (tool.Mode, target, stream.Entity);
        }
    }
}
