namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// A larva grows by eating corpses and, once it has enough biomass, evolves into its first caste
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugLarvaComponent : Component
{
    [DataField]
    public int Biomass;

    [DataField]
    public int BiomassNeeded = 5;
}
