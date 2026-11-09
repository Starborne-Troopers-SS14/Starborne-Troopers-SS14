using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Living barricade: bugs standing close lock their bodies into a wall. Bullets break on the wall,
/// the damage is shared between all bugs in it and none of them can walk until it falls apart
/// </summary>
[RegisterComponent]
public sealed partial class ST14LivingBarricadeComponent : Component
{
    /// <summary>
    /// Most bugs in one wall, the one who called it included
    /// </summary>
    [DataField]
    public int MaxMembers = 4;

    /// <summary>
    /// How far the other bugs may stand to join, in tiles
    /// </summary>
    [DataField]
    public float JoinRange = 2.5f;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Cooldown every bug of the wall gets once it falls apart
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Invisible shield put around every bug of the wall, it catches the bullets
    /// </summary>
    [DataField]
    public EntProtoId Shield = "ST14LivingBarricadeShield";

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public SoundSpecifier? BreakSound;

    [DataField]
    public EntProtoId Action = "ST14ActionBugBarricade";

    [DataField]
    public EntityUid? ActionEntity;
}

/// <summary>
/// A bug locked into a living barricade
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ST14InBarricadeComponent : Component
{
    /// <summary>
    /// Every bug of the wall, this one included
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> Members = new();

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;

    [DataField]
    public EntityUid? Shield;
}

/// <summary>
/// The shield of a bug in a living barricade: bullets that hit it hurt the whole wall instead
/// </summary>
[RegisterComponent]
public sealed partial class ST14BarricadeShieldComponent : Component
{
    [DataField]
    public EntityUid Bug;
}
