using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Hive;

[Serializable, NetSerializable]
public enum ST14HiveEvolutionUiKey : byte
{
    Key,
}

/// <summary>
/// Why a caste can or cannot be picked right now
/// </summary>
[Serializable, NetSerializable]
public enum ST14CasteStatus : byte
{
    Available,
    Current,
    NotEnoughPoints,
    NotEnoughBiomass,
    TabClosed,
    SpeciesClosed,
    Unreachable,
}

[Serializable, NetSerializable]
public sealed class ST14CasteEntry(ProtoId<ST14HiveCastePrototype> caste, ST14CasteStatus status)
{
    public readonly ProtoId<ST14HiveCastePrototype> Caste = caste;
    public readonly ST14CasteStatus Status = status;
}

[Serializable, NetSerializable]
public sealed class ST14HiveEvolutionState(
    List<ST14CasteEntry> castes,
    int evolutionPoints,
    int hivePoints,
    HashSet<ST14HiveTab> openTabs,
    HashSet<ProtoId<ST14HiveCastePrototype>> openSpecies,
    bool isBrain,
    bool isLarva,
    int? larvaBiomass,
    int larvaBiomassNeeded) : BoundUserInterfaceState
{
    public readonly List<ST14CasteEntry> Castes = castes;
    public readonly int EvolutionPoints = evolutionPoints;
    public readonly int HivePoints = hivePoints;
    public readonly HashSet<ST14HiveTab> OpenTabs = openTabs;
    public readonly HashSet<ProtoId<ST14HiveCastePrototype>> OpenSpecies = openSpecies;
    public readonly bool IsBrain = isBrain;
    public readonly bool IsLarva = isLarva;
    public readonly int? LarvaBiomass = larvaBiomass;
    public readonly int LarvaBiomassNeeded = larvaBiomassNeeded;
}

[Serializable, NetSerializable]
public sealed class ST14HiveEvolveMessage(ProtoId<ST14HiveCastePrototype> caste) : BoundUserInterfaceMessage
{
    public readonly ProtoId<ST14HiveCastePrototype> Caste = caste;
}

[Serializable, NetSerializable]
public sealed class ST14HiveOpenTabMessage(ST14HiveTab tab) : BoundUserInterfaceMessage
{
    public readonly ST14HiveTab Tab = tab;
}

[Serializable, NetSerializable]
public sealed class ST14HiveOpenSpeciesMessage(ProtoId<ST14HiveCastePrototype> caste) : BoundUserInterfaceMessage
{
    public readonly ProtoId<ST14HiveCastePrototype> Caste = caste;
}
