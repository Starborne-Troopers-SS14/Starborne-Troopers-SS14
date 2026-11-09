using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Hive;

/// <summary>
/// A tab of the evolution menu. Every tab past the first has to be opened by the brain bug with hive points
/// </summary>
[Serializable, NetSerializable]
public enum ST14HiveTab : byte
{
    T1,
    T2,
    T3,
    T4,
    T5,
    X,
}

/// <summary>
/// Where a caste sits inside its tab: the main line of the tab goes in the centre, side lines to the left and right
/// </summary>
[Serializable, NetSerializable]
public enum ST14HiveColumn : byte
{
    Left,
    Center,
    Right,
}

/// <summary>
/// One caste of the hive: what a bug can turn into, from what, and what it costs
/// </summary>
[Prototype("st14HiveCaste")]
public sealed partial class ST14HiveCastePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    /// <summary>
    /// The mob a bug of this caste is. Null for castes nobody can evolve into, like the larva
    /// </summary>
    [DataField]
    public EntProtoId? Entity;

    /// <summary>
    /// The tier shown in the menu, T0 to T6
    /// </summary>
    [DataField]
    public int Tier;

    [DataField]
    public ST14HiveTab Tab = ST14HiveTab.T1;

    [DataField]
    public ST14HiveColumn Column = ST14HiveColumn.Center;

    /// <summary>
    /// Order inside the column, top to bottom
    /// </summary>
    [DataField]
    public int Order;

    /// <summary>
    /// Castes that can evolve into this one
    /// </summary>
    [DataField]
    public List<ProtoId<ST14HiveCastePrototype>> EvolvesFrom = new();

    /// <summary>
    /// Whether a larva can grow into this caste once it has eaten enough biomass
    /// </summary>
    [DataField]
    public bool FromLarva;

    /// <summary>
    /// Personal evolution points the bug spends to evolve into this caste. Growing out of a larva is free
    /// </summary>
    [DataField]
    public int Cost;

    /// <summary>
    /// Hive points the brain bug spends to open this species after its tab is open. Zero opens it together with the tab
    /// </summary>
    [DataField]
    public int HiveCost;
}

/// <summary>
/// Hive-wide numbers: what opening each tab costs and how the brain's points scale with the number of players
/// </summary>
[Prototype("st14HiveSettings")]
public sealed partial class ST14HiveSettingsPrototype : IPrototype
{
    public static readonly ProtoId<ST14HiveSettingsPrototype> Default = "Default";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Hive points to open each tab. T1 is always open
    /// </summary>
    [DataField]
    public Dictionary<ST14HiveTab, int> TabCosts = new();

    /// <summary>
    /// Multipliers of the points from a sucked brain, checked in order: the first one whose player limit fits is used
    /// </summary>
    [DataField]
    public List<ST14HiveOnlineMultiplier> OnlineMultipliers = new();
}

[DataDefinition]
public sealed partial class ST14HiveOnlineMultiplier
{
    /// <summary>
    /// Up to how many players online this multiplier applies. Null means any number
    /// </summary>
    [DataField]
    public int? MaxPlayers;

    [DataField]
    public float Multiplier = 1f;
}
