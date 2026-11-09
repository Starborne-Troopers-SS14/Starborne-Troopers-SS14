using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared.Chat.Prototypes;

/// <summary>
///     Sounds collection for each <see cref="EmotePrototype"/>.
///     Different entities may use different sounds collections.
/// </summary>
[Prototype]
public sealed partial class EmoteSoundsPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The name used for the Voice selector in the character editor UI.
    /// </summary>
    [DataField]
    public LocId VoiceSelectorName { get; private set; } = "humanoid-profile-editor-voice-none";

    /// <inheritdoc/>
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<EmoteSoundsPrototype>))]
    public string[]? Parents { get; private set; }

    /// <inheritdoc/>
    [AbstractDataField]
    [NeverPushInheritance]
    public bool Abstract { get; private set; }

    /// <summary>
    ///     Optional fallback sound that will play if collection
    ///     doesn't have specific sound for this emote id.
    /// </summary>
    [DataField("sound")]
    public SoundSpecifier? FallbackSound;

    /// <summary>
    ///     Optional audio params that will be applied to ALL sounds.
    ///     This will overwrite any params that may be set in sound specifiers.
    /// </summary>
    [DataField("params")]
    public AudioParams? GeneralParams;

    /// <summary>
    ///     Collection of emote prototypes and their sounds.
    /// </summary>
    [DataField]
    [AlwaysPushInheritance]
    public Dictionary<ProtoId<EmotePrototype>, SoundSpecifier> Sounds = new();

    /// <summary>
    /// ST14: the sex this voice belongs to; a character of another sex cannot pick it. Null fits everyone
    /// </summary>
    [DataField("sex")]
    public Content.Shared.Humanoid.Sex? ST14Sex;

    /// <summary>
    /// ST14: whether a character of this sex may speak with this voice
    /// </summary>
    public bool ST14Fits(Content.Shared.Humanoid.Sex sex)
    {
        return ST14Sex == null || sex == Content.Shared.Humanoid.Sex.Unsexed || ST14Sex == sex;
    }
}
