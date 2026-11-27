namespace Content.Shared._ST14.Decay;

/// <summary>
/// How far a building left without maintenance has rotted. Every building the troopers make rots this way: what they
/// built with the multitool, the bunker upgrades. Trenches rot by the tile, see <see cref="ST14TrenchRotComponent"/>.
/// Put on by ST14DecaySystem the first time the building goes without supplies; kept in the sector save
/// </summary>
[RegisterComponent]
public sealed partial class ST14DecayComponent : Component
{
    /// <summary>
    /// How long the building has gone without maintenance in all
    /// </summary>
    [DataField]
    public TimeSpan Rot;

    /// <summary>
    /// Whether a supply point kept it the last time it was looked at
    /// </summary>
    [ViewVariables]
    public bool Supplied = true;
}

/// <summary>
/// A supply point of maintenance: a bunker base or a maintenance tunnel. It keeps the buildings round it from rotting,
/// spending the maintenance supplies put in its storage. A building in reach of several takes the nearest with supplies
/// </summary>
[RegisterComponent]
public sealed partial class ST14MaintenanceProviderComponent : Component
{
    /// <summary>
    /// How far round it, in tiles, buildings are kept. Measured flat: a provider under the ground keeps what stands on
    /// the surface over it
    /// </summary>
    [DataField]
    public float Radius = 15f;

    /// <summary>
    /// The storage the supplies are kept in
    /// </summary>
    [DataField]
    public string Container = "storagebase";

    /// <summary>
    /// What is left of the last supply opened: one supply keeps many buildings for a while
    /// </summary>
    [DataField]
    public float Credit;
}

/// <summary>
/// On the surface map of a sector: how long each trench tile dug in it has gone without maintenance. A trench that rots
/// through caves in
/// </summary>
[RegisterComponent]
public sealed partial class ST14TrenchRotComponent : Component
{
    [DataField]
    public Dictionary<Vector2i, float> Rot = new();
}
