/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._ST14.ZLevels.Core.Components;

/// <summary>
/// Allows an entity to move up and down the z-levels by gravity or jumping
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class ST14ZPhysicsComponent : Component
{
    /// <summary>
    /// The current speed of movement between z-levels.
    /// If greater than 0, the entity moves upward. If less than 0, the entity moves downward.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Velocity;

    /// <summary>
    /// The current height of the entity within the current Z-level.
    /// Takes values from 0 to 1. If the value rises above 1, the entity moves up to the next level and the value is normalized.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public float LocalPosition;

    /// Optimization Caches
    /// <summary>
    /// Cached value of the current z-level map height
    /// </summary>
    [DataField, AutoNetworkedField]
    public int CurrentZLevel;

    // Physics

    [DataField, AutoNetworkedField]
    public float Bounciness = 0.3f;

    [DataField, AutoNetworkedField]
    public float GravityMultiplier = 1f;

    [DataField, AutoNetworkedField]
    public bool Fallable = true;

    // Visuals



    /// <summary>
    /// The original DrawDepth of the object is automatically saved here. Increases by 1 when the creature has <see cref="LocalPosition"/> > 0
    /// </summary>
    [DataField]
    public int DrawDepthDefault;

    /// <summary>
    /// When the mapinit entity is created, its initial Sprite Offset value is written here in order to apply an offset based on the Z position relative to this value.
    /// </summary>
    [DataField]
    public Vector2 SpriteOffsetDefault = Vector2.Zero;

    /// <summary>
    /// automatically rises if the current localPosition is lower than the height. Enabled by default, but for ghosts, for example, there is no point in climbing stairs
    /// </summary>
    [DataField]
    public bool AutoStep = true;

    #region Gravity

    [DataField]
    public bool VelocityGravity = true;

    /// <summary>
    /// If true, ST14GetZVelocityEvent is raised every physics tick so systems (e.g. flight) can adjust Velocity.
    /// Off by default to avoid the event overhead for entities that never need custom vertical control.
    /// </summary>
    [DataField]
    public bool VelocityRaiseEvent;

    #endregion

    #region Cache

    [ViewVariables]
    public Vector2i? CachedTile;

    /// <summary>
    /// Cached value of the current distance to the ground in the current z-level. Updates only on MoveEvent and when tiles below change.
    /// </summary>
    [ViewVariables]
    public float CachedGroundHeight;

    /// <summary>
    /// Cached value of whether the entity is currently on sticky ground (ladders).
    /// </summary>
    [ViewVariables]
    public bool CachedStickyGround;

    /// <summary>
    /// ST14: the grid or map the cached tile belongs to. The cache is only reused while the body stays
    /// on the same tile of the same parent.
    /// </summary>
    [ViewVariables]
    public EntityUid CachedTileParent;

    /// <summary>
    /// ST14: true when the cached ground came from a high-ground entity (stairs, ladders). That height
    /// depends on the exact position inside the tile and on the vertical velocity, so it is recomputed
    /// on every move instead of being reused for the whole tile.
    /// </summary>
    [ViewVariables]
    public bool CachedGroundDynamic;

    /// <summary>
    /// ST14: value of the system's ground-cache epoch when the ground was cached.
    /// </summary>
    [ViewVariables]
    public int CachedGroundEpoch = -1;

    /// <summary>
    /// ST14: whether the uid is currently stored in the system's active-body list (which is compacted lazily).
    /// </summary>
    [ViewVariables]
    public bool InActiveList;

    /// <summary>
    /// ST14: networked fields changed during this Update's physics substeps, dirtied once at the end.
    /// </summary>
    [ViewVariables]
    public bool PendingDirtyVelocity;

    [ViewVariables]
    public bool PendingDirtyPosition;

    #endregion

    #region Sleep

    [DataField]
    public float SleepTimer;

    [ViewVariables]
    public bool Sleeping;

    [DataField]
    public float SleepThreshold = 0.3f;

    [DataField]
    public float TimeToSleep = 2f;

    #endregion

    /// <summary>
    /// While true, ProcessZPhysics skips this entity entirely: no gravity, no ground sync, no BodyStatus sync.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Disabled;
}
