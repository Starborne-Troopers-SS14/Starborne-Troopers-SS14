using System.Text;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Components;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Bugs and people do not understand each other's speech: a bug hears a trooper as noise and a trooper hears a bug
/// as chitter. Ghosts and anything that is not a living mob understand everyone
/// </summary>
public sealed partial class ST14HiveLanguageSystem : EntitySystem
{
    private static readonly string[] BugSyllables = { "кх", "тсс", "щёлк", "скр", "ххш", "трр", "кл", "ик", "чк", "ззт" };
    private static readonly string[] HumanSyllables = { "бу", "гра", "ду", "ох", "рам", "ву", "на", "го", "ба", "хм" };

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
    /// What a listener of the other kind hears instead. Each word turns into the same noise every time,
    /// so the same phrase sounds the same, and the length of the speech stays about the same
    /// </summary>
    public string Garble(EntityUid speaker, string message)
    {
        var syllables = IsBug(speaker) ? BugSyllables : HumanSyllables;
        var builder = new StringBuilder();

        foreach (var word in message.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
                builder.Append(' ');

            var hash = 17;
            foreach (var c in word.ToLowerInvariant())
            {
                hash = unchecked(hash * 31 + c);
            }

            var count = Math.Clamp((word.Length + 2) / 3, 1, 4);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append('-');

                builder.Append(syllables[(int) ((uint) unchecked(hash + i * 7919) % syllables.Length)]);
            }

            // keep the end of the sentence, a question still sounds like one
            var last = word[^1];
            if (last is '!' or '?' or '.' or ',')
                builder.Append(last);
        }

        return builder.ToString();
    }

    private bool IsBug(EntityUid uid)
    {
        return HasComp<ST14HiveMemberComponent>(uid) || HasComp<ST14HiveQueenComponent>(uid);
    }
}
