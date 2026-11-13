using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared._ST14.Hive;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// A character is either a trooper or a bug. The bug is its own species in the character editor, and each side
/// can only take its own jobs: bugs the hive jobs of the bug lobby theme, troopers everything else
/// </summary>
public static class ST14CharacterSide
{
    public static readonly ProtoId<SpeciesPrototype> BugSpecies = "ST14Bug";

    public static bool IsBug(string species)
    {
        return species == BugSpecies.Id;
    }

    public static bool IsBugJob(IPrototypeManager protoMan, ProtoId<JobPrototype> job)
    {
        return protoMan.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs) && bugs.Jobs.Contains(job);
    }

    /// <summary>
    /// Whether a job can be taken by a character of this species
    /// </summary>
    public static bool Fits(IPrototypeManager protoMan, string species, ProtoId<JobPrototype> job)
    {
        return IsBugJob(protoMan, job) == IsBug(species);
    }

    /// <summary>
    /// Old characters picked the bug side only through a hive job. A trooper whose wanted jobs are all hive jobs
    /// becomes a bug, so it keeps them after the side check
    /// </summary>
    public static string Migrate(IPrototypeManager protoMan, string species, IReadOnlyDictionary<ProtoId<JobPrototype>, JobPriority> jobs)
    {
        if (IsBug(species) || !protoMan.HasIndex(BugSpecies))
            return species;

        var wanted = jobs.Where(p => p.Value != JobPriority.Never).Select(p => p.Key).ToList();
        return wanted.Count > 0 && wanted.All(job => IsBugJob(protoMan, job)) ? BugSpecies.Id : species;
    }

    /// <summary>
    /// Drops the jobs of the other side
    /// </summary>
    public static void FilterJobs(IPrototypeManager protoMan, string species, Dictionary<ProtoId<JobPrototype>, JobPriority> jobs)
    {
        foreach (var job in jobs.Keys.ToList())
        {
            if (!Fits(protoMan, species, job))
                jobs.Remove(job);
        }
    }

    #region Bug names

    /// <summary>
    /// Bugs have no names of their own: the callsign of their caste and the number of the character, like "Воин-394".
    /// The profile keeps the default callsign, in the round the caste of the body gives it
    /// </summary>
    public const string DefaultCallsign = "st14-bug-callsign-default";

    public const int MaxBugNumber = 999;

    private static readonly Regex BugNameRegex = new(@"^(?<prefix>[^\-]+)-(?<number>\d{1,3})$");

    public static string BugName(string callsign, int number)
    {
        return $"{callsign}-{Math.Clamp(number, 1, MaxBugNumber)}";
    }

    public static string ProfileBugName(int number)
    {
        return BugName(Loc.GetString(DefaultCallsign), number);
    }

    /// <summary>
    /// The number of a bug name, false if the name is not one
    /// </summary>
    public static bool TryParseBugName(string name, out int number)
    {
        number = 0;
        var match = BugNameRegex.Match(name.Trim());
        return match.Success && int.TryParse(match.Groups["number"].Value, out number) && number is >= 1 and <= MaxBugNumber;
    }

    public static string RandomBugName(IRobustRandom random)
    {
        return ProfileBugName(random.Next(1, MaxBugNumber + 1));
    }

    /// <summary>
    /// Name of a bug body of a caste for the character number
    /// </summary>
    public static string CasteBugName(IPrototypeManager protoMan, ProtoId<ST14HiveCastePrototype>? caste, int number)
    {
        var callsign = caste is { } id && protoMan.TryIndex(id, out var proto) && proto.Callsign is { } loc
            ? Loc.GetString(loc)
            : Loc.GetString(DefaultCallsign);
        return BugName(callsign, number);
    }

    #endregion
}
