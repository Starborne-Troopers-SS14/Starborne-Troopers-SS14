using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Last rush: a blow that would kill the bug does not. Instead it rages for a few seconds, untouchable and
/// hitting twice as hard, and only then dies. Once per life
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class ST14LastRushComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(4);

    [DataField]
    public float DamageMultiplier = 2f;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField, AutoNetworkedField]
    public bool Used;

    [DataField, AutoNetworkedField]
    public bool Active;

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan End;
}
