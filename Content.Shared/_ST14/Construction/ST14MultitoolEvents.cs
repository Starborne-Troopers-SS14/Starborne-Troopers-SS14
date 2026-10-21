using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Construction;

[Serializable, NetSerializable]
public enum ST14MultitoolMode : byte
{
    None,
    Build,
    Dismantle,
}

[Serializable, NetSerializable]
public sealed class ST14StartBuildingEvent : EntityEventArgs
{
    public NetEntity Tool;
    public NetEntity Target;
    public ST14MultitoolMode Mode;
}

[Serializable, NetSerializable]
public sealed class ST14StopBuildingEvent : EntityEventArgs
{
    public NetEntity Tool;
}

[Serializable, NetSerializable]
public sealed class ST14PlaceBlueprintEvent : EntityEventArgs
{
    public ProtoId<ST14EngineerBuildablePrototype> Buildable;
    public NetCoordinates Coordinates;
    public Direction Direction;
}
