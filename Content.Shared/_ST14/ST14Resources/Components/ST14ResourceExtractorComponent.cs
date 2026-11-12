using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._ST14.ST14Resources.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ST14ResourceExtractorComponent : Component
{
    public const int SlotCount = 2;

    public static readonly string[] CapsuleSlotIds = { "capsule_slot_1", "capsule_slot_2" };

    [DataField(required: true)]
    public ProtoId<ST14ResourcePrototype> ResourceType;

    [DataField(required: true)]
    public FixedPoint2 ExtractionRate;

    [DataField]
    public TimeSpan HitInterval = TimeSpan.FromSeconds(1);

    [DataField]
    public bool RequireField = true;

    [DataField]
    public float FieldSearchRange = 0.5f;

    [DataField]
    public ItemSlot CapsuleSlot1 = new();

    [DataField]
    public ItemSlot CapsuleSlot2 = new();

    [DataField, AutoNetworkedField]
    public int ActiveSlot;

    [DataField, AutoNetworkedField]
    public bool AutoSwitch = true;

    [DataField, AutoNetworkedField]
    public bool Extracting;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextHitTime;

    [DataField]
    public SoundSpecifier? HitSound;

    /// <summary>
    /// Играть HitSound и в момент запуска. Нужно, когда звук — это один цикл анимации:
    /// анимация начинается с запуском, и без этого первый цикл шёл бы без звука.
    /// </summary>
    [DataField]
    public bool HitSoundOnStart;

    [DataField]
    public float SoundRadius = 6f;

    [DataField]
    public float ShakeRadius = 4f;

    [DataField]
    public float ShakeStrength = 0.06f;
}
