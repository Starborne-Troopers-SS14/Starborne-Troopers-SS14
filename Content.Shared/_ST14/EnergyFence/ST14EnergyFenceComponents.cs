using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.EnergyFence;

/// <summary>
/// Энергопилон. Запитанный пилон тянет луч к ближайшему запитанному пилону на одной прямой (по X или Y)
/// </summary>
[RegisterComponent]
public sealed partial class ST14EnergyPylonComponent : Component
{
    /// <summary>
    /// Наибольшее число тайлов между двумя связанными пилонами (считая тайл второго пилона)
    /// </summary>
    [DataField]
    public int MaxLength = 7;

    /// <summary>
    /// Пилон запитан, если в этом радиусе (в тайлах) стоит заякоренный энергощиток с заряженным энергоблоком
    /// </summary>
    [DataField]
    public float PowerRange = 10f;

    /// <summary>
    /// Сегмент луча для связи по оси X
    /// </summary>
    [DataField]
    public EntProtoId BeamHorizontal = "ST14EnergyBeamHorizontal";

    /// <summary>
    /// Сегмент луча для связи по оси Y
    /// </summary>
    [DataField]
    public EntProtoId BeamVertical = "ST14EnergyBeamVertical";

    /// <summary>
    /// Энергощиток, от которого сейчас питается пилон
    /// </summary>
    [ViewVariables]
    public EntityUid? Box;

    /// <summary>
    /// Сколько лучей сейчас держит пилон
    /// </summary>
    [ViewVariables]
    public int Links;
}

/// <summary>
/// Энергощиток: источник питания пилонов, пока в нём стоит заряженный энергоблок
/// </summary>
[RegisterComponent]
public sealed partial class ST14EnergyBoxComponent : Component
{
    /// <summary>
    /// Слот ItemSlots, в который вставляется энергоблок
    /// </summary>
    [DataField]
    public string BlockSlot = "energy_block";
}

/// <summary>
/// Один тайл луча между двумя пилонами. Бьёт током тех, кто подходит под белый список
/// </summary>
[RegisterComponent]
public sealed partial class ST14EnergyBeamComponent : Component
{
    [DataField]
    public DamageSpecifier Damage = new();

    /// <summary>
    /// Пока цель стоит в луче, удар повторяется через это время
    /// </summary>
    [DataField]
    public TimeSpan HitDelay = TimeSpan.FromSeconds(1);

    [DataField]
    public TimeSpan StunTime = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// На сколько тайлов отбрасывает цель от луча
    /// </summary>
    [DataField]
    public float ThrowDistance = 1.5f;

    /// <summary>
    /// Заряд энергоблока, который тратит один удар
    /// </summary>
    [DataField]
    public float ChargePerHit = 20f;

    /// <summary>
    /// Кого бьёт луч. Пусто — бьёт всех
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Кого луч пропускает всегда
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    [DataField]
    public SoundSpecifier? HitSound;

    [DataField]
    public EntProtoId? HitEffect = "EffectSparks";

    /// <summary>
    /// Пилон, от энергощитка которого питается луч
    /// </summary>
    [ViewVariables]
    public EntityUid? Source;

    /// <summary>
    /// Кто сейчас стоит в луче
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Contacts = new();

    /// <summary>
    /// Луч вдоль оси Y
    /// </summary>
    [ViewVariables]
    public bool Vertical;
}

[Serializable, NetSerializable]
public enum ST14EnergyPylonVisuals : byte
{
    Powered,
    Linked,
}

[Serializable, NetSerializable]
public enum ST14EnergyBoxVisuals : byte
{
    HasBlock,
    Active,
}
