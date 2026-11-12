namespace Content.Shared._ST14.Weapons;

/// <summary>
/// Where the muzzle flash of this gun appears: at the tip of the barrel instead of the default half tile.
/// Distances go from the holder's centre to the barrel tip, in tiles, along the shot direction.
/// </summary>
[RegisterComponent]
public sealed partial class ST14MuzzleOffsetComponent : Component
{
    /// <summary>
    /// Barrel tip when the gun is held in one hand.
    /// </summary>
    [DataField]
    public float Distance = 0.25f;

    /// <summary>
    /// Barrel tip when the gun is wielded with both hands.
    /// </summary>
    [DataField]
    public float WieldedDistance = 0.5f;

    /// <summary>
    /// How far the bright core of the flash sprite sits behind the sprite centre, in tiles.
    /// The ST14 flashes are drawn with the core at x=4 of 32, so the core lands on the tip.
    /// </summary>
    [DataField]
    public float FlashCoreOffset = 0.36f;
}
