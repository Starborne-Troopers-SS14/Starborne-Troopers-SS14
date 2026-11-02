using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._ST14.Sectors;

/// <summary>
/// A kind of ground a sector of a war planet can have. The first time a sector of that kind is fought over one of these
/// maps is picked at random, from then on the sector keeps it in its save
/// </summary>
[Prototype("st14SectorTerrain")]
public sealed partial class ST14SectorTerrainPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The terrain index of the sectors on the galaxy map, from 0
    /// </summary>
    [DataField(required: true)]
    public int Index;

    /// <summary>
    /// Map files of the sectors of this kind
    /// </summary>
    [DataField(required: true)]
    public List<ResPath> Maps = new();
}

/// <summary>
/// Rules of the sector maps that do not belong to one terrain
/// </summary>
[Prototype("st14SectorSettings")]
public sealed partial class ST14SectorSettingsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Queens put into the queen places of a sector map at the start of a round
    /// </summary>
    [DataField]
    public int Queens = 2;

    /// <summary>
    /// The queen put into the queen places. Plain id: the hive may not be on this server
    /// </summary>
    [DataField]
    public string? Queen;

    /// <summary>
    /// Mobs of this faction are the hive: the round goes on while one of them lives
    /// </summary>
    [DataField]
    public string HiveFaction = "Xeno";

    /// <summary>
    /// No side loses before this much of the round has passed, so a round does not end before everyone has spawned
    /// </summary>
    [DataField]
    public TimeSpan GracePeriod = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How often the round checks whether one side is wiped out
    /// </summary>
    [DataField]
    public TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A battle not decided by this time is an attack that failed
    /// </summary>
    [DataField]
    public TimeSpan RoundLimit = TimeSpan.FromHours(4);

    /// <summary>
    /// Components of the things a saved sector does not keep. Corpses, ghosts and spent casings go anyway
    /// </summary>
    [DataField]
    public List<string> TrashComponents = new();

    /// <summary>
    /// Tags of the decals a saved sector does not keep. Plain strings: blood decals may not be on this server
    /// </summary>
    [DataField]
    public List<string> TrashDecalTags = new();
}
