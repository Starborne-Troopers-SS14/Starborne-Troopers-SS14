using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._ST14.ZLevelShooting;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14ZProjectileComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField]
    public Vector2 Origin;

    [DataField, AutoNetworkedField]
    public float Distance;

    [DataField, AutoNetworkedField]
    public float From;

    [DataField, AutoNetworkedField]
    public float To;
}
