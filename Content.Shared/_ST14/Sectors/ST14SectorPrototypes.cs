using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Sectors;

/// <summary>
/// A kind of ground a sector of a war planet can have. Every sector of that kind is fought on one of these maps:
/// the sector number picks which, so a sector always looks the same and its neighbours differ
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
    /// Game maps the sectors of this kind are fought on. Plain ids: game maps are not loaded on the client
    /// </summary>
    [DataField(required: true)]
    public List<string> Maps = new();
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

    [DataField]
    public EntProtoId Queen = "ST14MobBugQueen";

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
}
