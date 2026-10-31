using Robust.Shared.GameStates;

namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// Swarm resonance: every other resonating bug nearby makes this one hit harder and run faster.
/// At full resonance the air around the bug trembles
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class ST14SwarmResonanceComponent : Component
{
    /// <summary>
    /// How far the other bugs count, in tiles
    /// </summary>
    [DataField]
    public float Range = 4f;

    /// <summary>
    /// Extra melee damage for each bug in range, 0.06 is six percent
    /// </summary>
    [DataField]
    public float DamagePerAlly = 0.06f;

    [DataField]
    public float SpeedPerAlly = 0.04f;

    [DataField]
    public int MaxAllies = 5;

    /// <summary>
    /// Resonating bugs in range right now, never more than <see cref="MaxAllies"/>
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Allies;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField, AutoPausedField]
    public TimeSpan NextUpdate;
}
