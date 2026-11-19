namespace Content.Shared._ST14.Hellwalls;

[RegisterComponent]
public sealed partial class ST14HellWallComponent : Component
{
    [DataField]
    public string? Preview;

    [DataField]
    public string Key = "ST14HellWall";

    [DataField]
    public string Prefix = "wall";

    [DataField]
    public bool Landing;
}
