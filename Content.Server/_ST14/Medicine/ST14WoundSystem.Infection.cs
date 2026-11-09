using System.Linq;
using Content.Shared._ST14.Medicine;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// What the wounds do with time: an open wound goes bad, slowly under a dressing and twice as fast from bug claws,
/// burns with fever and at last poisons the blood; washing it stops that, an antibiotic heals it, a fever reducer only
/// hides it, and a dirty wound sewn shut keeps rotting under the stitches. A broken bone grows back by itself over half
/// an hour, in a minute and a half with osteogen; straight under a splint, crooked without one
/// </summary>
public sealed partial class ST14WoundSystem
{
    private static readonly ProtoId<DamageTypePrototype> Poison = "Poison";

    private const float InfectionOpen = 0.0011f;
    private const float InfectionCovered = 0.0004f;
    private const float InfectionSewnIn = 0.0008f;
    private const float Sepsis = 0.8f;
    private const float BoneNatural = 0.0005f;

    private void Infection(Entity<ST14WoundsComponent> ent, DrugEffects drugs)
    {
        var comp = ent.Comp;
        var worst = 0f;
        foreach (var wound in comp.Wounds)
        {
            if (!ST14Zones.IsOpen(wound.Kind))
                continue;

            var before = wound.Infection;
            float growth;
            if (wound.Sutured)
                growth = wound.Infection > 0.2f ? InfectionSewnIn : 0f;
            else if (wound.Clean)
                growth = 0f;
            else
                growth = (wound.Bandaged || wound.Packed ? InfectionCovered : InfectionOpen) * (wound.Dirty ? 2f : 1f);

            // an antibiotic in the blood kills the infection faster than it grows
            wound.Infection = Math.Clamp(wound.Infection + growth - drugs.AntiInfection, 0f, 1f);
            if (before < 0.3f && wound.Infection >= 0.3f)
                _popup.PopupEntity(Loc.GetString("st14-wound-infected", ("zone", ST14Zones.Name(wound.Zone))), ent, ent, PopupType.MediumCaution);

            worst = MathF.Max(worst, wound.Infection);
        }

        comp.Infection = worst;

        // the fever the body feels; a fever reducer hides it, the infection goes on underneath
        var fever = Math.Clamp((worst - 0.25f) / 0.6f, 0f, 1f);
        comp.Fever = fever * Math.Clamp(1f - drugs.FeverRelief, 0f, 1f);

        // the infection is in the blood now
        if (worst >= Sepsis)
        {
            var damage = new DamageSpecifier(_prototypes.Index(Poison), FixedPoint2.New(0.4f + (worst - Sepsis) * 2f));
            _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);
            if (_random.Prob(0.05f))
                _popup.PopupEntity(Loc.GetString("st14-wound-sepsis"), ent, ent, PopupType.LargeCaution);
        }
    }

    private void Bones(Entity<ST14WoundsComponent> ent, DrugEffects drugs)
    {
        var comp = ent.Comp;
        foreach (var wound in comp.Wounds.Where(w => w.Kind == ST14WoundKind.Fracture).ToList())
        {
            wound.Severity -= BoneNatural + drugs.BoneGrowth;
            if (wound.Severity > 0f)
                continue;

            var zone = ST14Zones.Name(wound.Zone);
            if (wound.Splinted)
            {
                comp.Wounds.Remove(wound);
                _popup.PopupEntity(Loc.GetString("st14-bone-healed", ("zone", zone)), ent, ent, PopupType.Medium);
                continue;
            }

            // nothing held it straight: it grew back crooked
            wound.Kind = ST14WoundKind.Malunion;
            wound.Severity = 1f;
            _popup.PopupEntity(Loc.GetString("st14-bone-malunion", ("zone", zone)), ent, ent, PopupType.MediumCaution);
        }
    }
}
