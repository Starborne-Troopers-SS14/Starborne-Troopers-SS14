namespace Content.Shared._ST14.Sectors;

/// <summary>
/// Stays on the sector map from round to round while the sector is being fought over: walls, turrets, drills,
/// hives. Once the sector is taken by one side its map starts clean again
/// </summary>
[RegisterComponent]
public sealed partial class ST14SectorPersistentComponent : Component;

/// <summary>
/// A place on a sector map a hive queen may be put in at the start of a round
/// </summary>
[RegisterComponent]
public sealed partial class ST14SectorQueenSlotComponent : Component;
