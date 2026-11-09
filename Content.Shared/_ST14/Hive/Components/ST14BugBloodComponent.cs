namespace Content.Shared._ST14.Hive.Components;

/// <summary>
/// A bug that bleeds green: every hit leaves a splash of blood on the ground as a decal, and its death a few more
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ST14BugBloodComponent : Component
{
    /// <summary>
    /// Decals with this tag are the splashes to pick from
    /// </summary>
    [DataField]
    public string DecalTag = "ST14BugBlood";

    /// <summary>
    /// How far from the bug a splash may land, in tiles
    /// </summary>
    [DataField]
    public float Spread = 0.5f;

    /// <summary>
    /// Shortest time between two splashes, so a burst of fire does not carpet the floor
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(0.2);

    [DataField]
    public int DeathSplashes = 3;

    [DataField, AutoPausedField]
    public TimeSpan NextSplash;
}
