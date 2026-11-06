using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._ST14.Medicine;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Traits.Assorted;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// Turns hits into wounds on the parts of a body and lives them out second by second: the blood runs out of
/// whatever nobody holds, a limb under a tourniquet slowly dies, pain grows into screams and collapses, the drugs in
/// the blood fight the pain and load the heart, and an overloaded or emptied heart stops until a medic restarts it
/// </summary>
public sealed partial class ST14WoundSystem : EntitySystem
{
    public const string MorphineId = "ST14Morphine";
    public const string NaloxoneId = "ST14Naloxone";
    public const string StimId = "ST14CombatStim";
    public const string KetamineId = "ST14Ketamine";
    public const string AdrenalineId = "Epinephrine";
    public const string TranexamicId = "TranexamicAcid";

    private static readonly ProtoId<DamageTypePrototype> Asphyxiation = "Asphyxiation";
    private static readonly EntProtoId ForcedSleep = "StatusEffectForcedSleeping";
    private static readonly SoundSpecifier BoneCrack = new SoundPathSpecifier("/Audio/Effects/bone_rattle.ogg", AudioParams.Default.WithPitchScale(0.55f).WithVolume(3f));
    private static readonly SoundSpecifier HeartStop = new SoundPathSpecifier("/Audio/_ST14/Medicine/flatline.wav", AudioParams.Default.WithVolume(-4f));

    private const int MaxWounds = 16;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private ST14DismembermentSystem _dismember = default!;
    [Dependency] private ST14DyingSystem _dying = default!;
    [Dependency] private ST14WoundEffectsSystem _effects = default!;

    /// <summary>
    /// When someone last pressed on the chest of a stopped heart: until then the body suffocates slower
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _cpr = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14WoundsComponent, DamageDealtEvent>(OnDamage);
        SubscribeLocalEvent<ST14WoundsComponent, MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<ST14WoundsComponent, TargetDefibrillatedEvent>(OnDefibrillated);
        SubscribeLocalEvent<ST14WoundsComponent, ExaminedEvent>(OnExamined);
    }

    #region Wounds

    /// <summary>
    /// The parts of the body that are still there: a torn-off limb takes no new wounds
    /// </summary>
    public HashSet<ST14BodyZone> PresentZones(EntityUid uid)
    {
        var categories = _body.EnumerateOrgans<OrganComponent>(uid).Select(o => o.Comp1.Category?.Id).ToHashSet();
        var zones = new HashSet<ST14BodyZone> { ST14BodyZone.Head, ST14BodyZone.Torso };
        foreach (var zone in ST14Zones.All)
        {
            if (ST14Zones.Category(zone) is { } category && categories.Contains(category))
                zones.Add(zone);
        }

        return zones;
    }

    private ST14BodyZone PickZone(EntityUid uid)
    {
        var zones = PresentZones(uid);
        var weights = new Dictionary<ST14BodyZone, float>
        {
            { ST14BodyZone.Head, 10f },
            { ST14BodyZone.Torso, 34f },
            { ST14BodyZone.ArmLeft, 14f },
            { ST14BodyZone.ArmRight, 14f },
            { ST14BodyZone.LegLeft, 14f },
            { ST14BodyZone.LegRight, 14f },
        };

        var total = zones.Sum(z => weights[z]);
        var roll = _random.NextFloat(total);
        foreach (var zone in zones)
        {
            roll -= weights[zone];
            if (roll <= 0f)
                return zone;
        }

        return ST14BodyZone.Torso;
    }

    private static float Damage(DamageSpecifier damage, string type)
    {
        return damage.DamageDict.TryGetValue(type, out var value) && value > 0 ? value.Float() : 0f;
    }

    private void OnDamage(Entity<ST14WoundsComponent> ent, ref DamageDealtEvent args)
    {
        if (_mobState.IsDead(ent))
            return;

        var slash = Damage(args.Damage, "Slash");
        var pierce = Damage(args.Damage, "Piercing");
        var blunt = Damage(args.Damage, "Blunt");
        var heat = Damage(args.Damage, "Heat");
        var total = slash + pierce + blunt + heat;
        if (total <= 0f)
            return;

        ent.Comp.PainSpike = MathF.Min(70f, ent.Comp.PainSpike + total * 1.1f);
        var zone = PickZone(ent);

        if (slash >= 5f)
            AddWound(ent, ST14WoundKind.Cut, zone, slash / 35f, 0.12f + slash * 0.035f, slash >= 18f && _random.Prob(0.25f));

        if (pierce >= 7f)
            AddWound(ent, ST14WoundKind.Puncture, zone, pierce / 40f, 0.15f + pierce * 0.03f, pierce >= 20f && _random.Prob(0.2f));

        if (heat >= 8f)
            AddWound(ent, ST14WoundKind.Burn, zone, heat / 40f, 0f, false);

        // a heavy blow breaks the bone of a limb
        if (blunt >= 14f && ST14Zones.IsLimb(zone) && _random.Prob(MathF.Min(0.6f, (blunt - 14f) * 0.05f)) &&
            !ent.Comp.Wounds.Any(w => w.Zone == zone && w.Kind == ST14WoundKind.Fracture))
        {
            AddWound(ent, ST14WoundKind.Fracture, zone, 1f, 0f, false);
            _audio.PlayPvs(BoneCrack, ent);
            _popup.PopupEntity(Loc.GetString("st14-wound-fracture-self", ("zone", ST14Zones.Name(zone))), ent, ent, PopupType.LargeCaution);
            _popup.PopupEntity(Loc.GetString("st14-wound-fracture-others", ("victim", ent.Owner)), ent, Robust.Shared.Player.Filter.PvsExcept(ent), true, PopupType.MediumCaution);
            ent.Comp.PainSpike = MathF.Min(90f, ent.Comp.PainSpike + 35f);
        }

        Dirty(ent);
    }

    /// <summary>
    /// Opens a wound, or makes the same open wound of the part worse
    /// </summary>
    public void AddWound(Entity<ST14WoundsComponent> ent, ST14WoundKind kind, ST14BodyZone zone, float severity, float bleed, bool arterial)
    {
        severity = Math.Clamp(severity, 0.05f, 1f);
        if (arterial)
            bleed *= 2.5f;

        var existing = ent.Comp.Wounds.FirstOrDefault(w => w.Zone == zone && w.Kind == kind && !w.Sutured);
        if (existing != null)
        {
            existing.Severity = MathF.Min(1f, existing.Severity + severity * 0.6f);
            existing.Arterial |= arterial;
            existing.Bleed = MathF.Min(existing.Arterial ? 4f : 2f, existing.Bleed + bleed * 0.7f);

            // fresh blood soaks through the dressing
            if (bleed > 0f)
            {
                existing.Bandaged = false;
                existing.Packed = false;
            }
        }
        else
        {
            if (ent.Comp.Wounds.Count >= MaxWounds)
                ent.Comp.Wounds.RemoveAt(ent.Comp.Wounds.FindIndex(w => w.Sutured || w.Bleed <= 0f) is var i and >= 0 ? i : 0);

            ent.Comp.Wounds.Add(new ST14Wound
            {
                Kind = kind,
                Zone = zone,
                Severity = severity,
                Bleed = MathF.Min(arterial ? 4f : 2f, bleed),
                Arterial = arterial,
            });
        }

        if (arterial)
            _popup.PopupEntity(Loc.GetString("st14-wound-arterial-self", ("zone", ST14Zones.Name(zone))), ent, ent, PopupType.LargeCaution);

        Dirty(ent);
    }

    /// <summary>
    /// Blood a wound lets out now, with what holds it
    /// </summary>
    public static float Effective(ST14WoundsComponent comp, ST14Wound wound)
    {
        if (wound.Sutured || wound.Bleed <= 0f || comp.Tourniquets.ContainsKey(wound.Zone))
            return 0f;

        var bleed = wound.Bleed;
        if (wound.Packed)
            bleed *= 0.12f;

        if (wound.Bandaged)
            bleed *= wound.Arterial || wound.Kind == ST14WoundKind.Stump ? 0.6f : 0.15f;

        return bleed;
    }

    /// <summary>
    /// Blood the part lets out with nothing holding it: what a tourniquet would stop
    /// </summary>
    public static float RawBleed(ST14WoundsComponent comp, ST14BodyZone zone)
    {
        return comp.Wounds.Where(w => w.Zone == zone && !w.Sutured).Sum(w => w.Bleed);
    }

    public static float ZoneBleed(ST14WoundsComponent comp, ST14BodyZone zone)
    {
        return comp.Wounds.Where(w => w.Zone == zone).Sum(w => Effective(comp, w));
    }

    /// <summary>
    /// The limb came off: its wounds and tourniquet go with it, a stump stays
    /// </summary>
    public void LoseLimb(EntityUid uid, ST14BodyZone zone, bool torn)
    {
        if (!TryComp<ST14WoundsComponent>(uid, out var comp))
            return;

        comp.Wounds.RemoveAll(w => w.Zone == zone);
        comp.Tourniquets.Remove(zone);
        comp.Wounds.Add(new ST14Wound
        {
            Kind = ST14WoundKind.Stump,
            Zone = zone,
            Severity = 1f,
            Bleed = torn ? 3.2f : 0.35f,
            Arterial = torn,
        });

        comp.PainSpike = MathF.Min(100f, comp.PainSpike + (torn ? 70f : 30f));
        Dirty(uid, comp);
        _effects.Refresh((uid, comp));
    }

    /// <summary>
    /// A prosthesis took the place of the stump
    /// </summary>
    public void LimbRestored(EntityUid uid, ST14BodyZone zone)
    {
        if (!TryComp<ST14WoundsComponent>(uid, out var comp))
            return;

        comp.Wounds.RemoveAll(w => w.Zone == zone);
        comp.Tourniquets.Remove(zone);
        Dirty(uid, comp);
        _effects.Refresh((uid, comp));
    }

    public bool HasStump(EntityUid uid, ST14BodyZone zone, out bool sutured)
    {
        sutured = false;
        if (!TryComp<ST14WoundsComponent>(uid, out var comp) ||
            comp.Wounds.FirstOrDefault(w => w.Zone == zone && w.Kind == ST14WoundKind.Stump) is not { } stump)
        {
            return false;
        }

        sutured = stump.Sutured;
        return true;
    }

    public void AddPain(EntityUid uid, float pain)
    {
        if (TryComp<ST14WoundsComponent>(uid, out var comp))
            comp.PainSpike = MathF.Min(100f, comp.PainSpike + pain);
    }

    /// <summary>
    /// Pain does not get through: morphine, ketamine or something numbing the nerves
    /// </summary>
    public bool IsNumb(EntityUid uid)
    {
        if (_status.TryEffectsWithComp<PainNumbnessStatusEffectComponent>(uid, out _))
            return true;

        return TryComp<ST14WoundsComponent>(uid, out var comp) && (comp.Morphine >= 0.45f || comp.Ketamine >= 0.3f);
    }

    #endregion

    #region Treatment

    /// <summary>
    /// Whether the treatment has anything to do on the part
    /// </summary>
    public static bool Needs(ST14WoundsComponent comp, ST14TreatmentKind kind, ST14BodyZone zone)
    {
        var open = comp.Wounds.Where(w => w.Zone == zone && w.Kind != ST14WoundKind.Fracture && !w.Sutured).ToList();
        return kind switch
        {
            ST14TreatmentKind.Tourniquet => ST14Zones.IsLimb(zone) && !comp.Tourniquets.ContainsKey(zone) && RawBleed(comp, zone) > 0.05f,
            ST14TreatmentKind.Bandage => open.Any(w => !w.Bandaged),
            ST14TreatmentKind.Hemostatic => open.Any(w => !w.Packed && w.Bleed > 0f && w.Kind != ST14WoundKind.Burn),
            ST14TreatmentKind.Splint => comp.Wounds.Any(w => w.Zone == zone && w.Kind == ST14WoundKind.Fracture && !w.Splinted),
            ST14TreatmentKind.Suture => open.Count > 0,
            _ => false,
        };
    }

    /// <summary>
    /// The parts the treatment can be used on, the one bleeding most first
    /// </summary>
    public static List<ST14BodyZone> ZonesFor(ST14WoundsComponent comp, ST14TreatmentKind kind)
    {
        return ST14Zones.All
            .Where(z => Needs(comp, kind, z))
            .OrderByDescending(z => RawBleed(comp, z))
            .ThenByDescending(z => comp.Wounds.Where(w => w.Zone == z).Sum(w => w.Severity))
            .ToList();
    }

    public void Treat(Entity<ST14WoundsComponent> ent, ST14TreatmentKind kind, ST14BodyZone zone)
    {
        var wounds = ent.Comp.Wounds.Where(w => w.Zone == zone).ToList();
        switch (kind)
        {
            case ST14TreatmentKind.Tourniquet:
                ent.Comp.Tourniquets[zone] = 0f;
                break;

            case ST14TreatmentKind.Bandage:
                foreach (var wound in wounds.Where(w => w.Kind != ST14WoundKind.Fracture && !w.Sutured))
                {
                    wound.Bandaged = true;

                    // a dressing pressed on a small wound stops it outright
                    if (!wound.Arterial && wound.Kind != ST14WoundKind.Stump && wound.Bleed < 0.6f)
                        wound.Bleed *= 0.25f;
                }

                break;

            case ST14TreatmentKind.Hemostatic:
                foreach (var wound in wounds.Where(w => w.Kind != ST14WoundKind.Fracture && w.Kind != ST14WoundKind.Burn && !w.Sutured))
                {
                    wound.Packed = true;
                    wound.Bleed *= wound.Arterial ? 0.5f : 0.3f;
                }

                break;

            case ST14TreatmentKind.Splint:
                foreach (var wound in wounds.Where(w => w.Kind == ST14WoundKind.Fracture))
                {
                    wound.Splinted = true;
                }

                break;

            case ST14TreatmentKind.Suture:
                foreach (var wound in wounds.Where(w => w.Kind != ST14WoundKind.Fracture))
                {
                    wound.Sutured = true;
                    wound.Bandaged = false;
                    wound.Packed = false;
                    wound.Bleed = 0f;
                    wound.Arterial = false;
                }

                break;
        }

        Dirty(ent);
        _effects.Refresh(ent);
    }

    public bool RemoveTourniquet(Entity<ST14WoundsComponent> ent, ST14BodyZone zone)
    {
        if (!ent.Comp.Tourniquets.Remove(zone))
            return false;

        // the blood rushes back into the limb
        if (RawBleed(ent.Comp, zone) > 0.05f)
            ent.Comp.PainSpike = MathF.Min(100f, ent.Comp.PainSpike + 20f);

        Dirty(ent);
        return true;
    }

    #endregion

    #region Heart

    public void StopHeart(Entity<ST14WoundsComponent> ent)
    {
        if (ent.Comp.Heart == ST14HeartState.Arrest || _mobState.IsDead(ent))
            return;

        ent.Comp.Heart = ST14HeartState.Arrest;
        ent.Comp.Pulse = 0;
        ent.Comp.ArrhythmiaTime = 0f;
        RemComp<ST14DyingComponent>(ent);
        _status.TryUpdateStatusEffectDuration(ent, ForcedSleep, TimeSpan.FromSeconds(4));
        _stun.TryKnockdown(ent.Owner, TimeSpan.FromSeconds(4), force: true);
        _audio.PlayPvs(HeartStop, ent);
        _popup.PopupEntity(Loc.GetString("st14-heart-stopped-self"), ent, ent, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("st14-heart-stopped-others", ("victim", ent.Owner)), ent, Robust.Shared.Player.Filter.PvsExcept(ent), true, PopupType.LargeCaution);
        Dirty(ent);
    }

    public bool RestartHeart(Entity<ST14WoundsComponent> ent)
    {
        if (ent.Comp.Heart != ST14HeartState.Arrest || _mobState.IsDead(ent))
            return false;

        ent.Comp.Heart = ST14HeartState.Normal;
        ent.Comp.Pulse = 90;
        ent.Comp.HeartLoad = MathF.Min(ent.Comp.HeartLoad, 0.6f);
        _status.TryRemoveStatusEffect(ent, ForcedSleep);
        _cpr.Remove(ent);
        _chat.TryEmoteWithChat(ent, "Gasp", ignoreActionBlocker: true, forceEmote: true);
        _popup.PopupEntity(Loc.GetString("st14-heart-restarted"), ent, PopupType.Large);

        if (_mobState.IsCritical(ent))
            _dying.StartFight(ent);

        Dirty(ent);
        return true;
    }

    /// <summary>
    /// One round of chest compressions on a stopped heart. Adrenaline in the blood makes it far likelier to start
    /// </summary>
    public bool Compress(Entity<ST14WoundsComponent> ent)
    {
        if (ent.Comp.Heart != ST14HeartState.Arrest)
            return false;

        _cpr[ent] = _timing.CurTime + TimeSpan.FromSeconds(5);
        if (_random.Prob(0.04f + 0.3f * ent.Comp.Adrenaline))
            RestartHeart(ent);

        return true;
    }

    private void OnDefibrillated(Entity<ST14WoundsComponent> ent, ref TargetDefibrillatedEvent args)
    {
        if (ent.Comp.Heart == ST14HeartState.Arrest && _random.Prob(0.85f))
            RestartHeart(ent);
    }

    private void OnMobState(Entity<ST14WoundsComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            ent.Comp.Pulse = 0;
            ent.Comp.Bleeding = 0f;
        }
        else if (args.OldMobState == MobState.Dead)
        {
            ent.Comp.Heart = ST14HeartState.Normal;
            ent.Comp.Pulse = 60;
        }

        Dirty(ent);
    }

    #endregion

    private void OnExamined(Entity<ST14WoundsComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        foreach (var (zone, seconds) in ent.Comp.Tourniquets)
        {
            args.PushMarkup(Loc.GetString(seconds >= ent.Comp.IschemiaTime ? "st14-examine-tourniquet-long" : "st14-examine-tourniquet",
                ("zone", ST14Zones.Name(zone)), ("minutes", (int) (seconds / 60f))));
        }

        foreach (var zone in ST14Zones.All)
        {
            var wounds = ent.Comp.Wounds.Where(w => w.Zone == zone).ToList();
            if (wounds.Count == 0)
                continue;

            var name = ST14Zones.Name(zone);
            if (wounds.Any(w => w.Kind == ST14WoundKind.Stump))
                args.PushMarkup(Loc.GetString("st14-examine-stump", ("zone", name)));

            var bleed = ZoneBleed(ent.Comp, zone);
            if (bleed >= 1f)
                args.PushMarkup(Loc.GetString("st14-examine-gushing", ("zone", name)));
            else if (bleed > 0.1f)
                args.PushMarkup(Loc.GetString("st14-examine-bleeding", ("zone", name)));

            if (wounds.Any(w => w.Bandaged || w.Packed))
                args.PushMarkup(Loc.GetString("st14-examine-bandaged", ("zone", name)));

            if (wounds.Any(w => w.Kind == ST14WoundKind.Fracture && w.Splinted))
                args.PushMarkup(Loc.GetString("st14-examine-splinted", ("zone", name)));
            else if (wounds.Any(w => w.Kind == ST14WoundKind.Fracture))
                args.PushMarkup(Loc.GetString("st14-examine-broken", ("zone", name)));
        }

        if (ent.Comp.Heart == ST14HeartState.Arrest && !_mobState.IsDead(ent))
            args.PushMarkup(Loc.GetString("st14-examine-no-pulse"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ST14WoundsComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var comp, out var blood))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + TimeSpan.FromSeconds(1);
            var ent = (uid, comp);
            if (_mobState.IsDead(uid))
                continue;

            var txa = Drugs(ent, blood);
            Bleed(ent, blood, txa);
            Tourniquets(ent);
            Pain(ent);
            Heart(ent, now);
            Voice(ent, now);

            _effects.Refresh(ent);
            Dirty(uid, comp);
        }
    }

    /// <summary>
    /// Reads the drugs in the blood: how hard each works, the antidote pulling morphine out, the crash coming after
    /// a stimulant, and what too much of each does
    /// </summary>
    private float Drugs(Entity<ST14WoundsComponent> ent, BloodstreamComponent blood)
    {
        var comp = ent.Comp;
        if (!_solutions.ResolveSolution(ent.Owner, blood.BloodSolutionName, ref blood.BloodSolution, out var solution))
            return 0f;

        float Amount(string id) => solution.GetTotalPrototypeQuantity(id).Float();

        var morphine = Amount(MorphineId);
        var naloxone = Amount(NaloxoneId);
        var stim = Amount(StimId);
        var ketamine = Amount(KetamineId);
        var adrenaline = Amount(AdrenalineId);
        var tranexamic = Amount(TranexamicId);

        // the antidote tears the morphine out, and the pain comes back all at once
        if (naloxone > 0f && morphine > 0f)
        {
            var pulled = MathF.Min(morphine, 4f);
            _solutions.RemoveReagent(blood.BloodSolution.Value, MorphineId, FixedPoint2.New(pulled));
            morphine -= pulled;
            comp.PainSpike = MathF.Min(100f, comp.PainSpike + 12f);
            if (morphine <= 0f)
                _popup.PopupEntity(Loc.GetString("st14-drug-naloxone"), ent, ent, PopupType.MediumCaution);
        }

        comp.Morphine = Math.Clamp(morphine / 10f, 0f, 1f);
        comp.Stim = Math.Clamp(stim / 8f, 0f, 1f);
        comp.Ketamine = Math.Clamp(ketamine / 10f, 0f, 1f);
        comp.Adrenaline = Math.Clamp(adrenaline / 10f, 0f, 1f);

        // the higher the stimulant took the body, the harder it falls when it runs out
        if (stim > 0.5f)
        {
            comp.StimDose = MathF.Max(comp.StimDose, stim);
        }
        else if (comp.StimDose > 0f)
        {
            comp.CrashLeft = 25f + comp.StimDose * 6f;
            comp.StimDose = 0f;
            _popup.PopupEntity(Loc.GetString("st14-drug-crash"), ent, ent, PopupType.MediumCaution);
        }

        comp.CrashLeft = MathF.Max(0f, comp.CrashLeft - 1f);
        comp.Crash = Math.Clamp(comp.CrashLeft / 40f, 0f, 1f);

        // too much morphine: the breathing slows down to nothing
        if (morphine >= 20f)
        {
            var damage = new DamageSpecifier(_prototypes.Index(Asphyxiation), FixedPoint2.New(1.5f + (morphine - 20f) * 0.15f));
            _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);
            if (_random.Prob(0.15f))
                _popup.PopupEntity(Loc.GetString("st14-drug-morphine-overdose"), ent, ent, PopupType.MediumCaution);
        }

        // ketamine puts the patient under: the surgery can start
        if (ketamine >= 6f)
            _status.TryUpdateStatusEffectDuration(ent, ForcedSleep, TimeSpan.FromSeconds(3));

        return Math.Clamp(tranexamic / 10f, 0f, 1f);
    }

    /// <summary>
    /// The blood leaves through everything nobody holds. Small wounds clot by themselves, tranexamic acid helps them
    /// </summary>
    private void Bleed(Entity<ST14WoundsComponent> ent, BloodstreamComponent blood, float tranexamic)
    {
        var comp = ent.Comp;
        var total = 0f;
        var healed = _damageable.GetTotalDamage(ent.Owner).Float() < 5f;

        foreach (var wound in comp.Wounds)
        {
            if (!wound.Sutured && !wound.Arterial && wound.Kind is ST14WoundKind.Cut or ST14WoundKind.Puncture)
            {
                var clot = (0.003f + 0.02f * tranexamic) * (wound.Bandaged || wound.Packed ? 3f : 1f);
                wound.Bleed = MathF.Max(0f, wound.Bleed - clot);
            }

            // a body the medicine healed has its small wounds closing fast
            if (healed && !wound.Arterial && wound.Kind is ST14WoundKind.Cut or ST14WoundKind.Puncture or ST14WoundKind.Burn)
            {
                wound.Bleed *= 0.8f;
                wound.Severity *= 0.9f;
            }

            if (wound.Kind is ST14WoundKind.Cut or ST14WoundKind.Puncture or ST14WoundKind.Burn && wound.Bleed <= 0.001f)
                wound.Severity -= wound.Sutured ? 0.004f : wound.Bandaged ? 0.0025f : 0.0015f;

            total += Effective(comp, wound) * (1f - 0.35f * tranexamic);
        }

        comp.Wounds.RemoveAll(w => w.Kind is ST14WoundKind.Cut or ST14WoundKind.Puncture or ST14WoundKind.Burn && w.Severity <= 0.02f);
        comp.Bleeding = MathF.Min(10f, total);

        if (comp.Bleeding > 0.01f)
            _bloodstream.TryBleedOut((ent.Owner, blood), FixedPoint2.New(comp.Bleeding));

        // the wounds bleed, not the stock bleeding on top of them
        if (blood.BleedAmount > 0f)
            _bloodstream.TryModifyBleedAmount((ent.Owner, blood), -blood.BleedAmount);

        comp.Blood = _bloodstream.GetBloodLevel((ent.Owner, blood));
    }

    /// <summary>
    /// A limb under a tourniquet gets no blood: first it goes numb and cold, then it dies and falls away
    /// </summary>
    private void Tourniquets(Entity<ST14WoundsComponent> ent)
    {
        var comp = ent.Comp;
        foreach (var zone in comp.Tourniquets.Keys.ToList())
        {
            var seconds = comp.Tourniquets[zone] + 1f;
            comp.Tourniquets[zone] = seconds;

            if (seconds >= comp.NecrosisTime)
            {
                comp.Tourniquets.Remove(zone);
                if (_dismember.Necrotize(ent.Owner, zone))
                {
                    _popup.PopupEntity(Loc.GetString("st14-tourniquet-necrosis-self", ("zone", ST14Zones.Name(zone))), ent, ent, PopupType.LargeCaution);
                    _popup.PopupEntity(Loc.GetString("st14-tourniquet-necrosis-others", ("victim", ent.Owner)), ent, Robust.Shared.Player.Filter.PvsExcept(ent), true, PopupType.MediumCaution);
                }

                continue;
            }

            if (seconds >= comp.IschemiaTime && (int) (seconds - comp.IschemiaTime) % 60 == 0)
                _popup.PopupEntity(Loc.GetString("st14-tourniquet-ischemia", ("zone", ST14Zones.Name(zone))), ent, ent, PopupType.MediumCaution);
        }
    }

    private void Pain(Entity<ST14WoundsComponent> ent)
    {
        var comp = ent.Comp;
        var raw = 0f;
        foreach (var wound in comp.Wounds)
        {
            var pain = wound.Kind switch
            {
                ST14WoundKind.Cut => 2f + 10f * wound.Severity,
                ST14WoundKind.Puncture => 3f + 14f * wound.Severity,
                ST14WoundKind.Burn => 2f + 16f * wound.Severity,
                ST14WoundKind.Fracture => wound.Splinted ? 8f : 26f,
                ST14WoundKind.Stump => wound.Sutured ? 10f : 36f,
                _ => 0f,
            };

            if (wound.Sutured && wound.Kind != ST14WoundKind.Stump)
                pain *= 0.4f;
            else if (wound.Bandaged || wound.Packed)
                pain *= 0.8f;

            raw += pain;
        }

        foreach (var seconds in comp.Tourniquets.Values)
        {
            raw += 8f + (seconds >= comp.IschemiaTime ? 22f : 0f);
        }

        raw += comp.PainSpike;
        comp.PainSpike = MathF.Max(0f, comp.PainSpike - 5f);

        var relief = 62f * comp.Morphine + 95f * comp.Ketamine + 15f * comp.Adrenaline;
        if (_status.TryEffectsWithComp<PainNumbnessStatusEffectComponent>(ent, out _))
            relief += 45f;

        comp.Pain = Math.Clamp(raw - relief, 0f, 100f);
    }

    /// <summary>
    /// The heart answers the pain, the blood loss and the drugs. Driven too hard for too long it goes out of rhythm and
    /// stops; a stopped heart suffocates the body unless someone presses on the chest
    /// </summary>
    private void Heart(Entity<ST14WoundsComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.Heart == ST14HeartState.Arrest)
        {
            comp.Pulse = 0;
            var pumped = _cpr.TryGetValue(ent, out var until) && now < until;
            var damage = new DamageSpecifier(_prototypes.Index(Asphyxiation), FixedPoint2.New(pumped ? 0.5f : 2.2f));
            _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false);
            _status.TryUpdateStatusEffectDuration(ent, ForcedSleep, TimeSpan.FromSeconds(3));

            // adrenaline alone sometimes kicks it back
            if (_random.Prob(0.03f * comp.Adrenaline))
                RestartHeart(ent);

            return;
        }

        var speedball = comp.Morphine > 0.3f && comp.Stim > 0.3f ? 0.2f : 0f;
        var doubled = comp.Stim > 0.25f && comp.Adrenaline > 0.25f ? 0.3f : 0f;
        var load = 0.55f * comp.Stim + 0.45f * comp.Adrenaline + comp.Pain / 100f * 0.2f + MathF.Max(0f, 0.85f - comp.Blood) * 1.2f
                   + doubled + speedball - (comp.Stim < 0.1f ? 0.25f * comp.Morphine : 0f);
        comp.HeartLoad = Math.Clamp(load, 0f, 1.5f);

        var pulse = 72f + comp.Pain * 0.35f + MathF.Max(0f, 1f - comp.Blood) * 130f + comp.Stim * 45f + comp.Adrenaline * 55f
                    - comp.Morphine * 16f - comp.Ketamine * 12f + _random.NextFloat(-3f, 3f);
        if (_mobState.IsCritical(ent))
            pulse = _random.NextFloat(42f, 58f);

        comp.Pulse = (int) Math.Clamp(pulse, 30f, 220f);
        comp.ArrhythmiaTime = comp.HeartLoad >= 0.95f ? comp.ArrhythmiaTime + 1f : MathF.Max(0f, comp.ArrhythmiaTime - 2f);
        comp.Heart = comp.ArrhythmiaTime >= 4f ? ST14HeartState.Arrhythmia : comp.Pulse >= 115 ? ST14HeartState.Racing : ST14HeartState.Normal;

        if (comp.Heart == ST14HeartState.Arrhythmia && _random.Prob(0.015f + 0.06f * (comp.HeartLoad - 0.95f)))
            StopHeart(ent);
        else if (comp.Blood < 0.45f && _random.Prob(0.02f))
            StopHeart(ent);
    }

    /// <summary>
    /// What pain sounds like: moans, then screams and a collapse when it is past bearing
    /// </summary>
    private void Voice(Entity<ST14WoundsComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (comp.Heart == ST14HeartState.Arrest || _mobState.IsIncapacitated(ent) || comp.Ketamine >= 0.6f)
            return;

        if (now >= comp.NextVoice)
        {
            if (comp.Pain >= 75f)
            {
                _chat.TryEmoteWithChat(ent, "Scream", ignoreActionBlocker: true, forceEmote: true);
                _jitter.DoJitter(ent, TimeSpan.FromSeconds(2), true, 10f, 6f);
                _popup.PopupEntity(Loc.GetString("st14-pain-unbearable"), ent, ent, PopupType.LargeCaution);
                comp.NextVoice = now + TimeSpan.FromSeconds(_random.NextFloat(6f, 10f));
            }
            else if (comp.Pain >= 45f)
            {
                _chat.TrySendInGameICMessage(ent, Loc.GetString("st14-pain-groan"), InGameICChatType.Emote, false, ignoreActionBlocker: true);
                comp.NextVoice = now + TimeSpan.FromSeconds(_random.NextFloat(12f, 20f));
            }
        }

        if (comp.Pain >= 90f && comp.Stim < 0.25f && now >= comp.NextCollapse)
        {
            _stun.TryKnockdown(ent.Owner, TimeSpan.FromSeconds(3), force: true);
            _popup.PopupEntity(Loc.GetString("st14-pain-shock"), ent, ent, PopupType.LargeCaution);
            comp.NextCollapse = now + TimeSpan.FromSeconds(_random.NextFloat(15f, 25f));
        }

        if (comp.Bleeding >= 1f && _random.Prob(0.12f))
        {
            var worst = ST14Zones.All.OrderByDescending(z => ZoneBleed(comp, z)).First();
            _popup.PopupEntity(Loc.GetString("st14-bleeding-gushing", ("zone", ST14Zones.Name(worst))), ent, ent, PopupType.MediumCaution);
        }
    }
}
