using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// A defibrillator charged by hand: the first click on the patient starts the charge, the second one fires it.
/// Fired in the green zone it heals far more, fired past it it also burns, and left to overload it shocks everyone around
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14FieldDefibComponent : Component
{
    /// <summary>
    /// Seconds to a full charge. At a full charge the defibrillator overloads
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ChargeTime = 2.4f;

    /// <summary>
    /// Where the green zone starts and ends, as a part of the full charge
    /// </summary>
    [DataField, AutoNetworkedField]
    public float GreenStart = 0.6f;

    [DataField, AutoNetworkedField]
    public float GreenEnd = 0.85f;

    /// <summary>
    /// Healed on top of the shock itself when it is fired in the green zone or past it
    /// </summary>
    [DataField(required: true)]
    public DamageSpecifier GoodHeal = default!;

    /// <summary>
    /// Burn of a shock fired past the green zone
    /// </summary>
    [DataField]
    public int HotShock = 10;

    [DataField]
    public int OverloadShock = 20;

    [DataField]
    public int OverloadSplashShock = 10;

    [DataField]
    public float OverloadRange = 2f;

    /// <summary>
    /// Shock and stun of a poke at something that is not a person, a bug for one
    /// </summary>
    [DataField]
    public int PokeShock = 10;

    [DataField]
    public TimeSpan PokeStun = TimeSpan.FromSeconds(1);

    [AutoNetworkedField, ViewVariables]
    public EntityUid? User;

    [AutoNetworkedField, ViewVariables]
    public EntityUid? Target;

    [AutoNetworkedField, ViewVariables]
    public TimeSpan ChargeStart;

    /// <summary>
    /// The damage of the user when the charge started: a hit on the user breaks it
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public FixedPoint2 UserDamage;
}
