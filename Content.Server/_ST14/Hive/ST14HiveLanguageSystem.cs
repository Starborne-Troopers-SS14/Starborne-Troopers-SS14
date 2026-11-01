using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Components;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Bugs and people do not understand each other's speech: neither sees the words of the other, only a line about the
/// sound it made, like a bug chittering or a trooper burbling. Ghosts and anything that is not a living mob understand
/// everyone
/// </summary>
public sealed partial class ST14HiveLanguageSystem : EntitySystem
{
    private const int BugNoises = 5;
    private const int HumanNoises = 4;

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
    /// The sound a listener of the other kind hears instead of the words, like "chitters" or "burbles something".
    /// The same phrase makes the same sound, a question sounds like a question
    /// </summary>
    public string Noise(EntityUid speaker, string message, bool whisper = false)
    {
        var hash = 17;
        foreach (var c in message.ToLowerInvariant())
        {
            hash = unchecked(hash * 31 + c);
        }

        var bug = IsBug(speaker);
        var index = (int) ((uint) hash % (bug ? BugNoises : HumanNoises)) + 1;
        var noise = Loc.GetString(bug ? $"st14-hive-noise-bug-{index}" : $"st14-hive-noise-human-{index}");

        if (whisper)
            noise = Loc.GetString("st14-hive-noise-whisper", ("noise", noise));

        if (message.TrimEnd().EndsWith('?'))
            noise = Loc.GetString("st14-hive-noise-question", ("noise", noise));

        return noise;
    }

    private bool IsBug(EntityUid uid)
    {
        return HasComp<ST14HiveMemberComponent>(uid) || HasComp<ST14HiveQueenComponent>(uid);
    }
}
