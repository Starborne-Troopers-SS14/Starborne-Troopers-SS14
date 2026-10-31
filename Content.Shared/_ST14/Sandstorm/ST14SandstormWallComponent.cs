using Robust.Shared.GameStates;

namespace Content.Shared._ST14.Sandstorm;

/// <summary>
/// A tile of the sandstorm that closes off the edge of the map. Its outer band is the fringe: troopers can wade a little
/// way in, bullets fly through and it does not block sight. Deeper in the storm is a wall nothing gets through or breaks.
/// The client paints the whole storm seamlessly across tiles and fills the screen with dust near it
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ST14SandstormWallComponent : Component
{
    /// <summary>
    /// How many tiles one repeat of the storm texture spans, it must match the states of the storm RSI
    /// </summary>
    public const int Period = 4;

    /// <summary>
    /// How many tiles deep the passable fringe reaches from open ground
    /// </summary>
    [DataField]
    public int FringeDepth = 1;

    /// <summary>
    /// Whether this tile is in the fringe, worked out by the server from its neighbours
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Fringe;

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
    /// Server side: the depth of this tile was worked out at least once
    /// </summary>
    [ViewVariables]
    public bool Computed;

    /// <summary>
    /// Client side: the layers already show the piece of the storm for this tile and the look of its depth
    /// </summary>
    [ViewVariables]
    public bool Tiled;
}
