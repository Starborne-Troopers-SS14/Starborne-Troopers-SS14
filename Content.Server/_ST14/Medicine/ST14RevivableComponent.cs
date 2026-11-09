namespace Content.Server._ST14.Medicine;

/// <summary>
/// A dead person who can still be brought back. Once the time runs out the body becomes unrevivable
/// </summary>
[RegisterComponent]
public sealed partial class ST14RevivableComponent : Component
{
    [DataField]
    public TimeSpan Window = TimeSpan.FromSeconds(120);

    [ViewVariables]
    public TimeSpan LostAt;
}
