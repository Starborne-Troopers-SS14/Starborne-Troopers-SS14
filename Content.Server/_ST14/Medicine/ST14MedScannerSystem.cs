using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._ST14.Medicine;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The field scanner: aimed at a patient it keeps showing them part by part, with the heart, the blood, the drugs
/// and what is dangerous right now. Also the shout for a medic every trooper has
/// </summary>
public sealed partial class ST14MedScannerSystem : EntitySystem
{
    /// <summary>
    /// The dose past which a drug starts to do harm, for the bars of the scanner
    /// </summary>
    private static readonly Dictionary<string, float> Overdoses = new()
    {
        { ST14WoundSystem.MorphineId, 20f },
        { ST14WoundSystem.StimId, 12f },
        { ST14WoundSystem.KetamineId, 18f },
        { ST14WoundSystem.NaloxoneId, 20f },
        { ST14WoundSystem.AdrenalineId, 30f },
        { ST14WoundSystem.TranexamicId, 25f },
        { "Bicaridine", 30f },
        { "Kelotane", 30f },
        { "Dermaline", 30f },
        { "Saline", 0f },
    };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ST14WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14MedScannerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ST14MedScannerComponent, UseInHandEvent>(OnUseInHand);

        SubscribeLocalEvent<ST14MedicCallerComponent, MapInitEvent>(OnCallerInit);
        SubscribeLocalEvent<ST14MedicCallerComponent, ST14CallMedicActionEvent>(OnCallMedic);
    }

    private void OnAfterInteract(Entity<ST14MedScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<ST14WoundsComponent>(target))
            return;

        args.Handled = true;
        Scan(ent, args.User, target);
    }

    private void OnUseInHand(Entity<ST14MedScannerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !HasComp<ST14WoundsComponent>(args.User))
            return;

        args.Handled = true;
        Scan(ent, args.User, args.User);
    }

    public void Scan(Entity<ST14MedScannerComponent> ent, EntityUid user, EntityUid patient)
    {
        ent.Comp.Patient = patient;
        ent.Comp.NextUpdate = TimeSpan.Zero;
        _audio.PlayPvs(ent.Comp.ScanSound, ent);
        _ui.OpenUi(ent.Owner, ST14MedScannerUiKey.Key, user);
        UpdateState(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        ClearCalls();

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ST14MedScannerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Patient == null || now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + TimeSpan.FromSeconds(1);
            if (!_ui.IsUiOpen(uid, ST14MedScannerUiKey.Key) || TerminatingOrDeleted(comp.Patient))
            {
                comp.Patient = null;
                continue;
            }

            UpdateState((uid, comp));
        }
    }

    private void UpdateState(Entity<ST14MedScannerComponent> ent)
    {
        if (ent.Comp.Patient is not { } patient || !TryComp<ST14WoundsComponent>(patient, out var wounds))
            return;

        var present = _wounds.PresentZones(patient);
        var missing = ST14Zones.All.Where(z => !present.Contains(z)).ToArray();

        var drugs = new List<ST14DrugReading>();
        if (TryComp<BloodstreamComponent>(patient, out var blood) &&
            _solutions.ResolveSolution(patient, blood.BloodSolutionName, ref blood.BloodSolution, out var solution))
        {
            foreach (var reagent in solution.Contents)
            {
                var id = reagent.Reagent.Prototype;
                if (id == "Blood" || reagent.Quantity <= 0)
                    continue;

                var overdose = Overdoses.GetValueOrDefault(id, 0f);
                var amount = reagent.Quantity.Float();
                drugs.Add(new ST14DrugReading(id, amount, overdose, overdose > 0f ? amount / overdose : 0f));
            }
        }

        var state = new ST14MedScannerState(
            GetNetEntity(patient),
            Name(patient),
            wounds.Wounds.Select(w => w.Copy()).ToArray(),
            new Dictionary<ST14BodyZone, float>(wounds.Tourniquets),
            missing,
            wounds.Pain,
            wounds.Pulse,
            wounds.Heart,
            wounds.Blood,
            wounds.Bleeding,
            wounds.HeartLoad,
            drugs.OrderByDescending(d => d.Strength).ToArray(),
            Warnings(wounds, drugs, patient).ToArray(),
            (byte) (_mobState.IsDead(patient) ? 2 : _mobState.IsCritical(patient) ? 1 : 0),
            wounds.IschemiaTime,
            wounds.NecrosisTime,
            InRange(ent, patient));

        _ui.SetUiState(ent.Owner, ST14MedScannerUiKey.Key, state);
    }

    private bool InRange(Entity<ST14MedScannerComponent> ent, EntityUid patient)
    {
        var from = _transform.GetMapCoordinates(ent.Owner);
        var to = _transform.GetMapCoordinates(patient);
        return from.MapId == to.MapId && (from.Position - to.Position).Length() <= ent.Comp.Range;
    }

    /// <summary>
    /// What the medic has to deal with first, the worst on top
    /// </summary>
    private IEnumerable<string> Warnings(ST14WoundsComponent wounds, List<ST14DrugReading> drugs, EntityUid patient)
    {
        if (wounds.Heart == ST14HeartState.Arrest && !_mobState.IsDead(patient))
            yield return Loc.GetString("st14-scan-warn-arrest");

        if (wounds.Bleeding >= 1f)
            yield return Loc.GetString("st14-scan-warn-bleeding");

        foreach (var (zone, seconds) in wounds.Tourniquets)
        {
            if (seconds >= wounds.IschemiaTime)
                yield return Loc.GetString("st14-scan-warn-ischemia", ("zone", ST14Zones.Name(zone)), ("left", (int) ((wounds.NecrosisTime - seconds) / 60f) + 1));
        }

        if (wounds.Blood < 0.7f)
            yield return Loc.GetString("st14-scan-warn-blood");

        if (wounds.Heart == ST14HeartState.Arrhythmia)
            yield return Loc.GetString("st14-scan-warn-arrhythmia");

        if (wounds.Stim > 0.25f && wounds.Adrenaline > 0.25f)
            yield return Loc.GetString("st14-scan-warn-stim-adrenaline");

        if (wounds.Stim > 0.3f && wounds.Morphine > 0.3f)
            yield return Loc.GetString("st14-scan-warn-speedball");

        foreach (var drug in drugs.Where(d => d.Strength >= 0.8f))
        {
            yield return Loc.GetString("st14-scan-warn-overdose", ("drug", drug.Reagent));
        }

        if (wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Stump && !w.Sutured))
            yield return Loc.GetString("st14-scan-warn-stump");

        if (wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Fracture && !w.Splinted))
            yield return Loc.GetString("st14-scan-warn-fracture");

        if (wounds.Ketamine >= 0.6f)
            yield return Loc.GetString("st14-scan-warn-anaesthesia");

        if (wounds.Crash > 0.2f)
            yield return Loc.GetString("st14-scan-warn-crash");
    }

    private void OnCallerInit(Entity<ST14MedicCallerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnCallMedic(Entity<ST14MedicCallerComponent> ent, ref ST14CallMedicActionEvent args)
    {
        if (args.Handled || _mobState.IsDead(ent))
            return;

        args.Handled = true;
        var calling = EnsureComp<ST14CallingMedicComponent>(ent);
        calling.Until = _timing.CurTime + ent.Comp.Duration;
        Dirty(ent, calling);

        _chat.TrySendInGameICMessage(ent, Loc.GetString("st14-medic-call-shout"), InGameICChatType.Speak, false, ignoreActionBlocker: true);
        _popup.PopupEntity(Loc.GetString("st14-medic-call-self"), ent, ent);
    }

    /// <summary>
    /// Stops showing a call once a medic treated the trooper or the time ran out
    /// </summary>
    public void ClearCalls()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ST14CallingMedicComponent>();
        while (query.MoveNext(out var uid, out var calling))
        {
            if (now >= calling.Until)
                RemCompDeferred<ST14CallingMedicComponent>(uid);
        }
    }
}
