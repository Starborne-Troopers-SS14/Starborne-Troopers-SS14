using Robust.Shared.Player;
using Content.Shared.IdentityManagement;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._ST14.Medicine;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The field scanner: aimed at a patient it keeps showing them part by part, with the heart, the blood, the drugs
/// and what is dangerous right now. Also the shout for a medic every trooper has
/// </summary>
public sealed partial class ST14MedScannerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
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
        SubscribeNetworkEvent<ST14MedicRespondEvent>(OnRespond);
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
        UpdateCallers();

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

                var profile = _wounds.DrugOf(id);
                var overdose = profile?.Overdose ?? 0f;
                var amount = reagent.Quantity.Float();
                drugs.Add(new ST14DrugReading(id, amount, overdose, overdose > 0f ? amount / overdose : 0f, profile?.Summary?.Id));
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

        // what the drugs do together
        foreach (var id in wounds.Interactions)
        {
            if (_prototypes.Index(id).Warning is { } warning)
                yield return Loc.GetString(warning);
        }

        foreach (var drug in drugs.Where(d => d.Strength >= 0.8f))
        {
            var name = _prototypes.TryIndex<ReagentPrototype>(drug.Reagent, out var reagent) ? reagent.LocalizedName : drug.Reagent;
            yield return Loc.GetString("st14-scan-warn-overdose", ("drug", name));
        }

        if (wounds.Toxin >= 0.15f)
            yield return Loc.GetString("st14-scan-warn-venom");

        if (wounds.Infection >= Sepsis)
            yield return Loc.GetString("st14-scan-warn-sepsis");
        else if (wounds.Infection >= 0.3f)
            yield return Loc.GetString(wounds.Fever < 0.1f ? "st14-scan-warn-infection-hidden" : "st14-scan-warn-infection");

        if (wounds.Wounds.Any(w => w.Sutured && w.Infection > 0.2f))
            yield return Loc.GetString("st14-scan-warn-sewn-dirty");

        if (wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Stump && !w.Sutured))
            yield return Loc.GetString("st14-scan-warn-stump");

        if (wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Fracture && !w.Splinted))
            yield return Loc.GetString("st14-scan-warn-fracture");

        if (wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Malunion))
            yield return Loc.GetString("st14-scan-warn-malunion");

        if (wounds.Dissociation >= 0.6f)
            yield return Loc.GetString("st14-scan-warn-anaesthesia");

        if (wounds.Crash > 0.2f)
            yield return Loc.GetString("st14-scan-warn-crash");
    }

    private const float Sepsis = 0.8f;

    private TimeSpan _nextBroadcast;

    /// <summary>
    /// The callers who were told their call stands, to tell them once when it is over
    /// </summary>
    private readonly HashSet<EntityUid> _told = new();

    private void OnCallerInit(Entity<ST14MedicCallerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextCheck = _timing.CurTime;
    }

    /// <summary>
    /// The wounded shout for a medic by themselves once a wound is bad enough and keep calling while nobody comes.
    /// The call ends once they are treated well enough or dead. Medics and the callers hear how it goes every second
    /// </summary>
    private void UpdateCallers()
    {
        var now = _timing.CurTime;
        if (now < _nextBroadcast)
            return;

        _nextBroadcast = now + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<ST14MedicCallerComponent, ST14WoundsComponent>();
        while (query.MoveNext(out var uid, out var caller, out var wounds))
        {
            var dead = _mobState.IsDead(uid);
            TryComp<ST14CallingMedicComponent>(uid, out var calling);

            if (!dead && Serious(uid, wounds))
            {
                if (calling == null)
                {
                    calling = EnsureComp<ST14CallingMedicComponent>(uid);
                    caller.NextShout = now;
                }

                calling.Until = now + TimeSpan.FromSeconds(30);

                // a responder that cannot come any more lets the call open again
                if (calling.Responder is { } gone && (TerminatingOrDeleted(gone) || _mobState.IsIncapacitated(gone) || !HasComp<ST14MedicHudComponent>(gone)))
                    calling.Responder = null;

                Dirty(uid, calling);

                // a shout while conscious and nobody answered yet
                if (calling.Responder == null && now >= caller.NextShout && !_mobState.IsIncapacitated(uid) && wounds.Heart != ST14HeartState.Arrest)
                {
                    caller.NextShout = now + caller.ShoutEvery;
                    _chat.TrySendInGameICMessage(uid, Loc.GetString("st14-medic-call-shout"), InGameICChatType.Speak, false, ignoreActionBlocker: true);
                    _popup.PopupEntity(Loc.GetString("st14-medic-call-self"), uid, uid);
                }
            }
            else if (calling != null && (dead || !NeedsMedic(uid, wounds)))
            {
                if (calling.Responder is { } medic && !dead)
                    _popup.PopupEntity(Loc.GetString("st14-medic-call-done", ("name", Identity.Name(uid, EntityManager))), medic, medic);

                RemCompDeferred<ST14CallingMedicComponent>(uid);
                calling = null;
            }

            if (TryComp<ActorComponent>(uid, out var actor))
                SendStatus(uid, calling, actor.PlayerSession);
        }

        BroadcastCalls();
    }

    private void SendStatus(EntityUid uid, ST14CallingMedicComponent? calling, ICommonSession session)
    {
        if (calling == null)
        {
            // once to say the call is over, then nothing
            if (_told.Remove(uid))
                RaiseNetworkEvent(new ST14MedicCallStatusEvent(false, null, 0f), session);

            return;
        }

        _told.Add(uid);
        string? name = null;
        var distance = 0f;
        if (calling.Responder is { } medic)
        {
            name = Identity.Name(medic, EntityManager);
            var from = _transform.GetMapCoordinates(uid);
            var to = _transform.GetMapCoordinates(medic);
            distance = from.MapId == to.MapId ? (from.Position - to.Position).Length() : -1f;
        }

        RaiseNetworkEvent(new ST14MedicCallStatusEvent(true, name, distance), session);
    }

    /// <summary>
    /// Every medic gets every call with where it is, so a far one can be found by its distance
    /// </summary>
    private void BroadcastCalls()
    {
        var calls = new List<ST14MedicCall>();
        var callQuery = EntityQueryEnumerator<ST14CallingMedicComponent>();
        while (callQuery.MoveNext(out var uid, out var calling))
        {
            var coords = _transform.GetMapCoordinates(uid);
            calls.Add(new ST14MedicCall(GetNetEntity(uid), Identity.Name(uid, EntityManager), (int) coords.MapId, coords.Position,
                GetNetEntity(calling.Responder), calling.Responder is { } medic ? Identity.Name(medic, EntityManager) : null));
        }

        var medics = EntityQueryEnumerator<ST14MedicHudComponent, ActorComponent>();
        while (medics.MoveNext(out _, out _, out var actor))
        {
            RaiseNetworkEvent(new ST14MedicCallsEvent(calls), actor.PlayerSession);
        }
    }

    /// <summary>
    /// A wound bad enough to shout for a medic
    /// </summary>
    private bool Serious(EntityUid uid, ST14WoundsComponent wounds)
    {
        return wounds.Bleeding > 0.3f ||
               wounds.Pain >= 50f ||
               wounds.Blood < 0.85f ||
               wounds.Heart == ST14HeartState.Arrest ||
               wounds.Infection >= 0.5f ||
               wounds.Toxin >= 0.3f ||
               wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Fracture && !w.Splinted || w.Kind == ST14WoundKind.Stump && !w.Sutured || w.Arterial) ||
               _mobState.IsCritical(uid) ||
               _damageable.GetTotalDamage(uid).Float() >= 45f;
    }

    /// <summary>
    /// Still worth a medic: the call holds until the trooper is treated past this
    /// </summary>
    private bool NeedsMedic(EntityUid uid, ST14WoundsComponent wounds)
    {
        return wounds.Bleeding > 0.1f ||
               wounds.Pain >= 35f ||
               wounds.Blood < 0.8f ||
               wounds.Heart == ST14HeartState.Arrest ||
               wounds.Infection >= 0.3f ||
               wounds.Toxin >= 0.15f ||
               wounds.Wounds.Any(w => w.Kind == ST14WoundKind.Fracture && !w.Splinted || w.Kind == ST14WoundKind.Stump && !w.Sutured) ||
               _mobState.IsCritical(uid);
    }

    /// <summary>
    /// A medic answers a call: the one named, or the nearest nobody took. Pressed again, gives his call up
    /// </summary>
    private void OnRespond(ST14MedicRespondEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } medic || !HasComp<ST14MedicHudComponent>(medic) || _mobState.IsIncapacitated(medic))
            return;

        var named = ev.Target is { } net ? GetEntity(net) : (EntityUid?) null;

        // his own call taken already: give it up
        var callQuery = EntityQueryEnumerator<ST14CallingMedicComponent>();
        while (callQuery.MoveNext(out var uid, out var calling))
        {
            if (calling.Responder != medic || named != null && named != uid)
                continue;

            calling.Responder = null;
            Dirty(uid, calling);
            _popup.PopupEntity(Loc.GetString("st14-medic-respond-cancel", ("name", Identity.Name(uid, EntityManager))), medic, medic);
            _popup.PopupEntity(Loc.GetString("st14-medic-respond-gone"), uid, uid, PopupType.MediumCaution);
            return;
        }

        EntityUid? target = null;
        if (named is { } chosen && HasComp<ST14CallingMedicComponent>(chosen))
        {
            target = chosen;
        }
        else
        {
            var from = _transform.GetMapCoordinates(medic);
            var best = float.MaxValue;
            var open = EntityQueryEnumerator<ST14CallingMedicComponent>();
            while (open.MoveNext(out var uid, out var calling))
            {
                if (calling.Responder != null || uid == medic)
                    continue;

                var at = _transform.GetMapCoordinates(uid);
                if (at.MapId != from.MapId)
                    continue;

                var distance = (at.Position - from.Position).LengthSquared();
                if (distance < best)
                {
                    best = distance;
                    target = uid;
                }
            }
        }

        if (target is not { } patient || !TryComp<ST14CallingMedicComponent>(patient, out var call))
        {
            _popup.PopupEntity(Loc.GetString("st14-medic-respond-none"), medic, medic);
            return;
        }

        call.Responder = medic;
        Dirty(patient, call);
        _popup.PopupEntity(Loc.GetString("st14-medic-respond-self", ("name", Identity.Name(patient, EntityManager))), medic, medic);
        _popup.PopupEntity(Loc.GetString("st14-medic-respond-patient", ("name", Identity.Name(medic, EntityManager))), patient, patient, PopupType.Large);
    }

    /// <summary>
    /// Ends calls nobody renewed
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
