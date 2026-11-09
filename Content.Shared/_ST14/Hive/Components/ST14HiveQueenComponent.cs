using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// A queen of the hive. It does nothing but lay larvae for ghosts to take
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ST14HiveQueenComponent : Component
{
    [DataField]
    public TimeSpan LayInterval = TimeSpan.FromMinutes(4);

    [DataField, AutoPausedField]
    public TimeSpan NextLay;

    /// <summary>
    /// The queen stops laying while this many of her larvae are still waiting for a player
    /// </summary>
    [DataField]
    public int MaxWaitingLarvae = 3;

    [DataField]
    public EntProtoId Larva = "ST14MobBugLarva";

    [DataField]
    public SoundSpecifier? LaySound = new SoundPathSpecifier("/Audio/_ST14/Mobs/Bugs/larva_laid.ogg");

    [ViewVariables]
    public List<EntityUid> Laid = new();
}
