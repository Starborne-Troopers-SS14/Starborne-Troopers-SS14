namespace Content.Shared._ST14.Sectors;

/// <summary>
/// A place on a sector map a hive queen may be put in when the sector starts from a fresh map
/// </summary>
[RegisterComponent]
public sealed partial class ST14SectorQueenSlotComponent : Component;

/// <summary>
/// A passage on an edge of a hex sector map into the neighbouring sector. Edges count counter-clockwise from the east:
/// 0 east, 1 north-east, 2 north-west, 3 west, 4 south-west, 5 south-east
/// </summary>
[RegisterComponent]
public sealed partial class ST14SectorGateComponent : Component
{
    [DataField(required: true)]
    public int Edge;
}

/// <summary>
/// Whose body this is in a sector save: the player comes back to it next round, in the same job
/// </summary>
[RegisterComponent]
public sealed partial class ST14SectorOwnerComponent : Component
{
    [DataField]
    public string UserId = string.Empty;

    [DataField]
    public string? Job;
}
