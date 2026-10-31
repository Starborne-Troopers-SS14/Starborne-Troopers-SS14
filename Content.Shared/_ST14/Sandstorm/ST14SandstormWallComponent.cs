namespace Content.Shared._ST14.Sandstorm;

/// <summary>
/// A tile of the sandstorm that closes off the edge of the map: nothing walks, shoots or sees through it and nothing
/// breaks it. The client paints the wall seamlessly across tiles and fills the screen with dust near it
/// </summary>
[RegisterComponent]
public sealed partial class ST14SandstormWallComponent : Component
{
    /// <summary>
    /// How many tiles one repeat of the storm texture spans, it must match the states of the storm RSI
    /// </summary>
    public const int Period = 4;

    /// <summary>
    /// From how far away, in tiles, the dust starts covering the screen
    /// </summary>
    [DataField]
    public float HazeRange = 6f;

    /// <summary>
    /// How thick the dust on the screen gets right at the wall, 0 to 1
    /// </summary>
    [DataField]
    public float HazeStrength = 0.55f;

    /// <summary>
    /// Client side: the layers already show the piece of the storm for this tile
    /// </summary>
    [ViewVariables]
    public bool Tiled;
}
