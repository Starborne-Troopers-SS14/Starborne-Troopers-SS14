using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// Look of the lobby for one side of the war. The lobby picks the theme whose jobs contain the job the selected character
/// has on high priority, the theme without jobs is the default
/// </summary>
[Prototype("st14LobbyTheme")]
public sealed partial class ST14LobbyThemePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<ST14HubScenePrototype> Scene;

    /// <summary>
    /// Jobs that switch the lobby to this theme
    /// </summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>
    /// Start of the locale keys of the panel titles and status lines, like "st14-lobby" for "st14-lobby-briefing"
    /// </summary>
    /// <summary>
    /// The job a character of this side gets when none of its wanted jobs is open, if the player asked for it
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? FallbackJob;

    [DataField]
    public string LocPrefix = "st14-lobby";

    /// <summary>
    /// Colour of the panel titles and the light on the character stand
    /// </summary>
    [DataField]
    public Color Accent = Color.FromHex("#F7D68C");

    [DataField]
    public Color StandLight = Color.FromHex("#5CBFE6");
}
