namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Biomass left in a corpse. Added on the first bite; the corpse is gone when it runs out
/// </summary>
[RegisterComponent]
public sealed partial class ST14BiomassComponent : Component
{
    [DataField]
    public int Remaining = 10;
}
