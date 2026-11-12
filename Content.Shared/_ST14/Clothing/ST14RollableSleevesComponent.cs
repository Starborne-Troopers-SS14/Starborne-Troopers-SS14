using Robust.Shared.GameStates;

namespace Content.Shared._ST14.Clothing;

/// <summary>
/// A uniform whose sleeves can be rolled up. Rolled sleeves use the equipped sprite with <see cref="RolledEquippedPrefix"/>.
/// With <c>FoldableClothing</c> (jacket off) the sleeves have nothing to roll, the jacketless sprite wins.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14RollableSleevesComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Rolled;

    /// <summary>
    /// Prefix of the equipped state with the sleeves rolled up, e.g. "sleeves" for "sleeves-equipped-INNERCLOTHING".
    /// </summary>
    [DataField]
    public string RolledEquippedPrefix = "sleeves";
}
