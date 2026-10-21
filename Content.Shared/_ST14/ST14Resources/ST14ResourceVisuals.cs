using Robust.Shared.Serialization;

namespace Content.Shared._ST14.ST14Resources;

[Serializable, NetSerializable]
public enum ST14ResourceExtractorVisuals : byte
{
    Extracting,
    Slot1Fill,
    Slot2Fill,
}

[Serializable, NetSerializable]
public enum ST14ResourceCapsuleVisuals : byte
{
    FillFraction,
    Color,
    Resource,
}
