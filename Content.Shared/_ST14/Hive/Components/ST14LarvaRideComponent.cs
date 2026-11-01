using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// The larva hops onto the back of a bigger bug and rides it into battle. Up there it takes no damage,
/// and it drops off when its mount dies. One larva per back
/// </summary>
[RegisterComponent]
public sealed partial class ST14LarvaRideComponent : Component
{
    /// <summary>
    /// How far the mount may stand, in tiles
    /// </summary>
    [DataField]
    public float Range = 3f;

    /// <summary>
    /// Where the larva sits on the mount
    /// </summary>
    [DataField]
    public Vector2 Offset = new(0f, 0.3f);

    /// <summary>
    /// Cooldown once the larva is back on the ground
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public EntProtoId Action = "ST14ActionLarvaRide";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// A larva on the back of another bug
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14RidingComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Mount;
}

/// <summary>
/// A bug with a larva on its back. Its sprite shows the larva, the larva itself is hidden
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14CarryingRiderComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Rider;
}
