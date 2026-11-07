using System.Linq;
using Content.Shared._ST14.Medicine;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The drugs in the blood, read every second from their <see cref="ST14DrugPrototype"/>: how hard each works, the
/// antidotes pulling others out, what too much of each does, the crash after the stimulants, and what the drugs do
/// together through <see cref="ST14DrugInteractionPrototype"/>
/// </summary>
public sealed partial class ST14WoundSystem
{
    /// <summary>
    /// Everything the drugs in the blood do this second, summed up
    /// </summary>
    private sealed class DrugEffects
    {
        public float Relief;
        public float Sedation;
        public float Stim;
        public float Adrenergic;
        public float Dissociation;
        public float Toxin;
        public float HeartLoad;
        public float Pulse;
        public float Bleeding;
        public float Clotting;
        public float Steady;
        public float BloodRestore;
        public float Regeneration;
        public float BoneGrowth;
        public float AntiInfection;
        public float FeverRelief;
        public float CrashRelief;
        public float RhythmGuard;
        public float RestartBonus;
        public float Breathing;
        public float Arrest;
    }

    private readonly Dictionary<string, ST14DrugPrototype> _drugs = new();
    private readonly List<ST14DrugInteractionPrototype> _interactions = new();
    private readonly Dictionary<string, float> _amounts = new();
    private readonly Dictionary<string, float> _strengths = new();

    private void InitializePharmacology()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        LoadDrugs();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ST14DrugPrototype>() || args.WasModified<ST14DrugInteractionPrototype>())
            LoadDrugs();
    }

    private void LoadDrugs()
    {
        _drugs.Clear();
        foreach (var drug in _prototypes.EnumeratePrototypes<ST14DrugPrototype>())
        {
            _drugs[drug.ID] = drug;
        }

        _interactions.Clear();
        _interactions.AddRange(_prototypes.EnumeratePrototypes<ST14DrugInteractionPrototype>());
    }

    /// <summary>
    /// The profile of the drug a reagent is, for the scanner
    /// </summary>
    public ST14DrugPrototype? DrugOf(string reagent)
    {
        return _drugs.Values.FirstOrDefault(d => d.Reagent == reagent);
    }

    private DrugEffects Pharmacology(Entity<ST14WoundsComponent> ent, BloodstreamComponent blood)
    {
        var fx = new DrugEffects();
        var comp = ent.Comp;
        comp.Interactions.Clear();

        if (!_solutions.ResolveSolution(ent.Owner, blood.BloodSolutionName, ref blood.BloodSolution, out var solution))
            return fx;

        _amounts.Clear();
        foreach (var drug in _drugs.Values)
        {
            var amount = solution.GetTotalPrototypeQuantity(drug.Reagent).Float();
            if (amount > 0.01f)
                _amounts[drug.ID] = amount;
        }

        // antidotes pull other reagents out of the blood
        foreach (var drug in _drugs.Values)
        {
            if (drug.Removes.Count == 0 || !_amounts.TryGetValue(drug.ID, out var antidote))
                continue;

            var strength = MathF.Min(1f, antidote / drug.FullDose);
            foreach (var (reagent, rate) in drug.Removes)
            {
                var present = solution.GetTotalPrototypeQuantity(reagent).Float();
                if (present <= 0f)
                    continue;

                var pulled = MathF.Min(present, rate * strength);
                _solutions.RemoveReagent(blood.BloodSolution!.Value, reagent, FixedPoint2.New(pulled));

                foreach (var other in _drugs.Values.Where(d => d.Reagent == reagent))
                {
                    if (!_amounts.ContainsKey(other.ID))
                        continue;

                    _amounts[other.ID] -= pulled;
                    if (_amounts[other.ID] <= 0.01f)
                        _amounts.Remove(other.ID);

                    // a painkiller torn out of the blood: the pain comes back all at once
                    if (other.PainRelief > 20f)
                    {
                        comp.PainSpike = MathF.Min(100f, comp.PainSpike + 12f);
                        if (present - pulled <= 0.01f)
                            _popup.PopupEntity(Loc.GetString("st14-drug-antidote-pain"), ent, ent, PopupType.MediumCaution);
                    }
                }
            }
        }

        _strengths.Clear();
        foreach (var (id, amount) in _amounts)
        {
            _strengths[id] = amount / _drugs[id].FullDose;
        }

        // what the drugs do together; some of them cancel each other out
        foreach (var interaction in _interactions)
        {
            if (interaction.Drugs.Count == 0 || !interaction.Drugs.All(d => _strengths.TryGetValue(d, out var s) && s >= interaction.MinStrength))
                continue;

            comp.Interactions.Add(interaction.ID);
            foreach (var (drug, factor) in interaction.Weakens)
            {
                if (_strengths.ContainsKey(drug))
                    _strengths[drug] *= factor;
            }
        }

        foreach (var (id, raw) in _strengths)
        {
            var drug = _drugs[id];
            var s = MathF.Min(raw, 1f);
            fx.Relief += drug.PainRelief * s;
            fx.Sedation += drug.Sedation * s;
            fx.Stim += drug.Stimulation * s;
            fx.Adrenergic += drug.Adrenergic * s;
            fx.Dissociation += drug.Dissociation * s;
            fx.Toxin += drug.Toxin * s;
            fx.HeartLoad += drug.HeartLoad * s;
            fx.Pulse += drug.Pulse * s;
            fx.Bleeding += drug.Bleeding * s;
            fx.Clotting += drug.Clotting * s;
            fx.Steady += drug.Steady * s;
            fx.BloodRestore += drug.BloodRestore * s;
            fx.Regeneration += drug.Regeneration * s;
            fx.BoneGrowth += drug.BoneGrowth * s;
            fx.AntiInfection += drug.AntiInfection * s;
            fx.FeverRelief += drug.FeverRelief * s;
            fx.CrashRelief += drug.CrashRelief * s;
            fx.RhythmGuard += drug.RhythmGuard * s;
            fx.RestartBonus += drug.RestartBonus * s;

            var amount = _amounts[id];
            if (drug.Overdose > 0f && amount > drug.Overdose)
            {
                var over = amount - drug.Overdose;
                fx.Breathing += drug.OverdoseBreathing * (1f + 0.1f * over);
                fx.Pulse += drug.OverdosePulse;
                fx.Arrest += drug.OverdoseArrest;
                if (drug.OverdoseDamage != null)
                    _damageable.TryChangeDamage(ent.Owner, drug.OverdoseDamage, ignoreResistances: true, interruptsDoAfters: false);

                if (drug.OverdosePopup is { } overdose && _random.Prob(0.15f))
                    _popup.PopupEntity(Loc.GetString(overdose), ent, ent, PopupType.MediumCaution);
            }

            // remember how high it took the body: the higher, the harder the fall
            if (drug.CrashAfter > 0f && amount > 0.5f)
                comp.CrashPeaks[id] = MathF.Max(comp.CrashPeaks.GetValueOrDefault(id), amount);
        }

        // the stimulants that wore off: now the crash
        foreach (var id in comp.CrashPeaks.Keys.ToList())
        {
            if (_amounts.TryGetValue(id, out var left) && left > 0.5f)
                continue;

            if (_drugs.TryGetValue(id, out var drug))
            {
                comp.CrashLeft += drug.CrashAfter * comp.CrashPeaks[id];
                if (drug.WearOffPopup is { } wearOff)
                    _popup.PopupEntity(Loc.GetString(wearOff), ent, ent, PopupType.MediumCaution);
            }

            comp.CrashPeaks.Remove(id);
        }

        foreach (var id in comp.Interactions)
        {
            var interaction = _prototypes.Index(id);
            fx.HeartLoad += interaction.HeartLoad;
            fx.Pulse += interaction.Pulse;
            fx.Breathing += interaction.Breathing;
            fx.Bleeding += interaction.Bleeding;
            fx.Clotting += interaction.Clotting;
            fx.Sedation += interaction.Sedation;
            fx.Dissociation += interaction.Dissociation;
            fx.Relief += interaction.PainRelief;
            fx.Arrest += interaction.Arrest;
            if (interaction.Popup is { } popup && _random.Prob(0.06f))
                _popup.PopupEntity(Loc.GetString(popup), ent, ent, PopupType.MediumCaution);
        }

        comp.Sedation = Math.Clamp(fx.Sedation, 0f, 1f);
        comp.Stim = Math.Clamp(fx.Stim, 0f, 1f);
        comp.Adrenaline = Math.Clamp(fx.Adrenergic, 0f, 1f);
        comp.Dissociation = Math.Clamp(fx.Dissociation, 0f, 1f);
        comp.Toxin = Math.Clamp(fx.Toxin, 0f, 1f);
        comp.Steady = MathF.Max(0f, fx.Steady);
        comp.Relief = fx.Relief;
        comp.RestartBonus = MathF.Max(0f, fx.RestartBonus);

        comp.CrashLeft = MathF.Max(0f, comp.CrashLeft - 1f - fx.CrashRelief);
        comp.Crash = Math.Clamp(comp.CrashLeft / 40f, 0f, 1f);

        // the breath slows down to nothing
        if (fx.Breathing > 0f)
        {
            var damage = new DamageSpecifier(_prototypes.Index(Asphyxiation), FixedPoint2.New(fx.Breathing));
            _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);
        }

        // deep enough dissociation is anaesthesia: the surgery can start
        if (comp.Dissociation >= 0.6f)
            _status.TryUpdateStatusEffectDuration(ent, ForcedSleep, TimeSpan.FromSeconds(3));

        return fx;
    }
}
