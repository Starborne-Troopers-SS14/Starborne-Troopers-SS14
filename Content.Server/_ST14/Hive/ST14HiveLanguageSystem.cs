using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Bugs and people do not understand each other's speech: neither sees a word of the other, they only hear the sound
/// of it, a trooper burbling or a bug chittering. Ghosts and anything that is not a living mob understand everyone
/// </summary>
public sealed partial class ST14HiveLanguageSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier BugTalk = new SoundCollectionSpecifier("ST14BugDroneAlarm", AudioParams.Default.WithVolume(-6f));
    private static readonly SoundSpecifier HumanTalk = new SoundPathSpecifier("/Audio/Voice/Talk/slime.ogg");
    private static readonly SoundSpecifier HumanAsk = new SoundPathSpecifier("/Audio/Voice/Talk/slime_ask.ogg");
    private static readonly SoundSpecifier HumanExclaim = new SoundPathSpecifier("/Audio/Voice/Talk/slime_exclaim.ogg");

    /// <summary>
    /// Whether the listener makes out what the speaker says
    /// </summary>
    public bool Understands(EntityUid? listener, EntityUid speaker)
    {
        if (listener is not { } uid || uid == speaker || !HasComp<MobStateComponent>(uid))
            return true;

        return IsBug(uid) == IsBug(speaker);
    }

    /// <summary>
    /// Whether the listener hears no speech at all, its ears ringing after the brain bug's scream
    /// </summary>
    public bool IsDeaf(EntityUid? listener)
    {
        return HasComp<ST14DeafenedComponent>(listener);
    }

    /// <summary>
    /// Plays the sound of the speech to one listener of the other kind, in place of the words
    /// </summary>
    public void PlayNoise(EntityUid speaker, string message, ICommonSession listener, bool whisper = false)
    {
        var trimmed = message.TrimEnd();
        var sound = IsBug(speaker) ? BugTalk
            : trimmed.EndsWith('?') ? HumanAsk
            : trimmed.EndsWith('!') ? HumanExclaim
            : HumanTalk;

        var audioParams = sound.Params;
        if (whisper)
            audioParams = audioParams.AddVolume(-8f).WithMaxDistance(3f);

        _audio.PlayEntity(sound, Filter.SinglePlayer(listener), speaker, false, audioParams);
    }

    private bool IsBug(EntityUid uid)
    {
        return HasComp<ST14HiveMemberComponent>(uid) || HasComp<ST14HiveQueenComponent>(uid);
    }
}
