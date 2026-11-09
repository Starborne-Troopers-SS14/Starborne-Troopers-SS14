using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Footsteps of a bug that depend on what it walks on. Surfaces are concrete, metal, metal_thin, sand and gore;
/// the tile's own footstep collection picks the surface
/// </summary>
[RegisterComponent]
public sealed partial class ST14BugFootstepsComponent : Component
{
    [DataField]
    public Dictionary<string, SoundSpecifier> Walk = new();

    [DataField]
    public Dictionary<string, SoundSpecifier> Run = new();

    /// <summary>
    /// Surface for tiles the bug does not know, and for open ground without a grid
    /// </summary>
    [DataField]
    public string DefaultSurface = "sand";

    [DataField]
    public float WalkDistance = 1.3f;

    [DataField]
    public float RunDistance = 1.8f;

    [ViewVariables]
    public EntityCoordinates? LastPosition;

    [ViewVariables]
    public float Distance;
}
