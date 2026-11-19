using System.Numerics;

namespace Content.Shared._ST14.Hellwalls;

[RegisterComponent]
public sealed partial class ST14DepthSortComponent : Component
{
    [DataField]
    public float Offset;

    [DataField]
    public List<Vector2> Extents = new();
}
