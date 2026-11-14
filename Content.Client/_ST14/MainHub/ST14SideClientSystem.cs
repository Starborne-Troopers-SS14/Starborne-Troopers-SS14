using Content.Shared._ST14.MainHub;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// The side the player is bound to in the war over the current region, as the server told it
/// </summary>
public sealed class ST14SideClientSystem : EntitySystem
{
    public ST14GalaxyFaction? Side { get; private set; }

    /// <summary>
    /// The code of the region fought for, empty between regions
    /// </summary>
    public string Region { get; private set; } = string.Empty;

    /// <summary>
    /// The server answered at least once: until then a missing side means nothing
    /// </summary>
    public bool Known { get; private set; }

    /// <summary>
    /// The player picked the side on the choice screen of this client, not the server binding them by their first job
    /// </summary>
    public bool ChosenHere { get; private set; }

    public event Action? Changed;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ST14SideStateEvent>(OnState);
    }

    private void OnState(ST14SideStateEvent ev)
    {
        Side = ev.Side;
        Region = ev.Region;
        Known = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// The species of the characters of the side the player is bound to, null while there is no side
    /// </summary>
    public string? SideSpecies => Side switch
    {
        ST14GalaxyFaction.Arachnid => ST14CharacterSide.BugSpecies.Id,
        ST14GalaxyFaction.Federation => HumanoidCharacterProfile.DefaultSpecies,
        _ => null,
    };

    /// <summary>
    /// The character can play for the side of the player, any character fits while there is no side
    /// </summary>
    public bool Fits(HumanoidCharacterProfile profile)
    {
        return SideSpecies is not { } species || profile.Species == species;
    }

    /// <summary>
    /// A new random character of the side of the player with the basic job of that side on high priority.
    /// Made like this the new character fits the side at once and can be selected, a random species could not
    /// </summary>
    public HumanoidCharacterProfile NewCharacter(IPrototypeManager protoMan, IRobustRandom random)
    {
        var species = SideSpecies ?? HumanoidCharacterProfile.DefaultSpecies;
        var profile = HumanoidCharacterProfile.RandomWithSpecies(species);
        if (ST14CharacterSide.IsBug(species))
            profile = profile.WithName(ST14CharacterSide.RandomBugName(random));

        if (ST14CharacterSide.FallbackJob(protoMan, species) is { } job)
            profile = profile.WithJobPriorities(new[] { new KeyValuePair<ProtoId<JobPrototype>, JobPriority>(job, JobPriority.High) });

        return profile;
    }

    public void Request()
    {
        RaiseNetworkEvent(new ST14SideRequestEvent());
    }

    public void Choose(ST14GalaxyFaction side)
    {
        ChosenHere = true;
        RaiseNetworkEvent(new ST14SideChooseEvent(side));
    }
}
