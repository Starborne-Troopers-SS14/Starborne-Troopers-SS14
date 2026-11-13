using Content.Shared._ST14.Medicine;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The station medicine is off for troopers: the stock brute and burn damage of a body is only what its open wounds
/// account for. A hit too light to open a wound, a bruise, and the damage of a wound that was treated or closed fade
/// by themselves, so nothing is left that only station medicine could heal. A body in critical state waits for a medic
/// </summary>
public sealed partial class ST14WoundSystem
{
    // damage one point of wound severity stands for, by the damage type the wound comes from
    private const float CutDamage = 35f;
    private const float PunctureDamage = 40f;
    private const float BurnDamage = 40f;
    private const float FractureDamage = 20f;
    private const float MalunionDamage = 10f;
    private const float StumpDamage = 30f;

    // stock damage without a wound behind it that fades every second
    private const float FadePerSecond = 1.5f;

    private static readonly string[] WoundDamageTypes = { "Slash", "Piercing", "Blunt", "Heat" };

    private void SyncDamage(Entity<ST14WoundsComponent> ent, DrugEffects drugs)
    {
        // a body in critical state is the medic's: it does not crawl out of it on its own
        if (_mobState.IsIncapacitated(ent) || !HasComp<DamageableComponent>(ent))
            return;

        var damage = _damageable.GetAllDamage(ent.Owner);

        var backed = new Dictionary<string, float>
        {
            ["Slash"] = 0f,
            ["Piercing"] = 0f,
            ["Blunt"] = 0f,
            ["Heat"] = 0f,
        };

        foreach (var wound in ent.Comp.Wounds)
        {
            switch (wound.Kind)
            {
                case ST14WoundKind.Cut:
                    backed["Slash"] += wound.Severity * CutDamage;
                    break;
                case ST14WoundKind.Puncture:
                    backed["Piercing"] += wound.Severity * PunctureDamage;
                    break;
                case ST14WoundKind.Burn:
                    backed["Heat"] += wound.Severity * BurnDamage;
                    break;
                case ST14WoundKind.Fracture:
                    backed["Blunt"] += FractureDamage;
                    break;
                case ST14WoundKind.Malunion:
                    backed["Blunt"] += MalunionDamage;
                    break;
                case ST14WoundKind.Stump:
                    backed["Slash"] += StumpDamage;
                    break;
            }
        }

        var fade = FadePerSecond * (1f + MathF.Max(0f, drugs.Regeneration));
        DamageSpecifier? heal = null;
        foreach (var type in WoundDamageTypes)
        {
            if (!damage.DamageDict.TryGetValue(type, out var current))
                continue;

            var excess = current.Float() - backed[type];
            if (excess <= 0.01f)
                continue;

            heal ??= new DamageSpecifier();
            heal.DamageDict[type] = -FixedPoint2.New(MathF.Min(excess, fade));
        }

        if (heal != null)
            _damageable.TryChangeDamage(ent.Owner, heal, ignoreResistances: true, interruptsDoAfters: false);
    }
}
