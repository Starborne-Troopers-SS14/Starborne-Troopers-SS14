using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hellwalls;

[RegisterComponent]
public sealed partial class ST14RailingSpawnerComponent : Component
{
    [DataField]
    public EntProtoId Railing = "ST14WallRailing";

    [DataField]
    public Vector2 Lift = new(0f, 0.25f);
}
