namespace Content.Shared._ST14.Terrain;

/// <summary>
/// A pit that swallows whatever walks into it, using the entity's chasm settings for the fall.
/// Unlike a plain chasm it only triggers when the centre of the walker is inside one of the pit's fixtures,
/// so pits with uneven outlines do not grab people standing next to them
/// </summary>
[RegisterComponent]
public sealed partial class ST14PitComponent : Component
{
    /// <summary>
    /// Entities touching the pit's fixtures right now, with how many fixture contacts they have
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, int> Touching = new();
}
