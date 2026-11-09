using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._ST14.Medicine;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Standing;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The hands of a medic: tourniquets, dressings, hemostatic gauze, splints and sutures put on a patient part by part,
/// tourniquets taken off, a pulse checked and a stopped heart pumped by hand. One click treats the part that needs it
/// most, the right-click menu picks the part
/// </summary>
public sealed partial class ST14TreatmentSystem : EntitySystem
{
    public static readonly VerbCategory Medicine = new("st14-verb-category-medicine", "/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png");

    private static readonly EntProtoId Tourniquet = "ST14Tourniquet";
    private static readonly SoundSpecifier Compression = new SoundPathSpecifier("/Audio/Effects/thudswoosh.ogg", AudioParams.Default.WithVolume(-2f).WithVariation(0.1f));
    private static readonly SoundSpecifier Unstrap = new SoundPathSpecifier("/Audio/Items/zip.ogg", AudioParams.Default.WithPitchScale(1.3f));

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private ST14WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14TreatmentComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ST14TreatmentComponent, ST14TreatDoAfterEvent>(OnTreated);
        SubscribeLocalEvent<ST14WoundsComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ST14WoundsComponent, ST14RemoveTourniquetDoAfterEvent>(OnTourniquetRemoved);
        SubscribeLocalEvent<ST14WoundsComponent, ST14CprDoAfterEvent>(OnCompressed);
    }

    private static string Action(ST14TreatmentKind kind)
    {
        return $"st14-treat-{kind.ToString().ToLowerInvariant()}";
    }

    private void OnAfterInteract(Entity<ST14TreatmentComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !TryComp<ST14WoundsComponent>(target, out var wounds))
            return;

        args.Handled = true;
        var zones = ST14WoundSystem.ZonesFor(wounds, ent.Comp.Kind);
        if (zones.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("st14-treat-nothing", ("item", ent.Owner)), target, args.User);
            return;
        }

        Start(ent, args.User, target, zones[0]);
    }

    /// <summary>
    /// Begins treating the part: the hands work for a few seconds, and what hurts makes the patient scream
    /// </summary>
    public bool Start(Entity<ST14TreatmentComponent> item, EntityUid user, EntityUid target, ST14BodyZone zone)
    {
        if (item.Comp.NeedsLying && !_standing.IsDown(target))
        {
            _popup.PopupEntity(Loc.GetString("st14-treat-lie-down"), target, user, PopupType.MediumCaution);
            return false;
        }

        var self = user == target;
        var doAfter = new DoAfterArgs(EntityManager, user, self ? item.Comp.SelfDelay : item.Comp.Delay, new ST14TreatDoAfterEvent(zone), item, target, item)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        var zoneName = ST14Zones.Name(zone);
        var action = Loc.GetString(Action(item.Comp.Kind));
        _popup.PopupEntity(Loc.GetString("st14-treat-start-self", ("action", action), ("zone", zoneName), ("target", target)), target, user);
        _popup.PopupEntity(Loc.GetString("st14-treat-start-others", ("user", user), ("action", action), ("zone", zoneName), ("target", target)),
            target, Filter.PvsExcept(user), true);

        if (item.Comp.StartSound != null)
            _audio.PlayPvs(item.Comp.StartSound, target);

        // a tourniquet pulled tight, gauze pushed into a wound, a needle without anaesthesia: it hurts
        if (item.Comp.Pain > 0f && !_wounds.IsNumb(target) && !_mobState.IsIncapacitated(target))
        {
            _wounds.AddPain(target, item.Comp.Pain);
            if (item.Comp.Pain >= 30f)
                _chat.TryEmoteWithChat(target, "Scream", ignoreActionBlocker: true, forceEmote: true);
        }

        return true;
    }

    private void OnTreated(Entity<ST14TreatmentComponent> ent, ref ST14TreatDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || !TryComp<ST14WoundsComponent>(target, out var wounds))
            return;

        args.Handled = true;
        if (!ST14WoundSystem.Needs(wounds, ent.Comp.Kind, args.Zone))
            return;

        _wounds.Treat((target, wounds), ent.Comp.Kind, args.Zone);
        if (ent.Comp.EndSound != null)
            _audio.PlayPvs(ent.Comp.EndSound, target);

        _popup.PopupEntity(Loc.GetString($"st14-treat-done-{ent.Comp.Kind.ToString().ToLowerInvariant()}", ("zone", ST14Zones.Name(args.Zone)), ("target", target)),
            target, PopupType.Medium);

        if (ent.Comp.Reusable)
            return;

        if (TryComp<StackComponent>(ent, out var stack))
            _stack.ReduceCount((ent.Owner, stack), 1);
        else
            QueueDel(ent);
    }

    private void OnGetVerbs(Entity<ST14WoundsComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;
        var target = ent.Owner;

        // the tool in hand, on each part that needs it
        if (args.Using is { } used && TryComp<ST14TreatmentComponent>(used, out var treatment))
        {
            var item = (used, treatment);
            var priority = 10;
            foreach (var zone in ST14WoundSystem.ZonesFor(ent.Comp, treatment.Kind))
            {
                var picked = zone;
                args.Verbs.Add(new Verb
                {
                    Text = Loc.GetString("st14-treat-verb", ("action", Loc.GetString(Action(treatment.Kind))), ("zone", ST14Zones.Name(zone))),
                    Category = Medicine,
                    Priority = priority--,
                    Act = () => Start(item, user, target, picked),
                });
            }
        }

        foreach (var (zone, seconds) in ent.Comp.Tourniquets)
        {
            var picked = zone;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("st14-treat-remove-tourniquet", ("zone", ST14Zones.Name(zone)), ("minutes", (int) (seconds / 60f))),
                Category = Medicine,
                Priority = -1,
                Act = () => StartRemove(user, target, picked),
            });
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("st14-treat-pulse"),
            Category = Medicine,
            Priority = -5,
            Act = () => CheckPulse(user, ent),
        });

        if (ent.Comp.Heart == ST14HeartState.Arrest && !_mobState.IsDead(target) && user != target)
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("st14-treat-cpr"),
                Category = Medicine,
                Priority = 20,
                Act = () => StartCompressions(user, target),
            });
        }
    }

    private void StartRemove(EntityUid user, EntityUid target, ST14BodyZone zone)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, 2f, new ST14RemoveTourniquetDoAfterEvent(zone), target, target)
        {
            BreakOnMove = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("st14-treat-removing", ("zone", ST14Zones.Name(zone))), target, user);
    }

    private void OnTourniquetRemoved(Entity<ST14WoundsComponent> ent, ref ST14RemoveTourniquetDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!_wounds.RemoveTourniquet(ent, args.Zone))
            return;

        _audio.PlayPvs(Unstrap, ent);
        var bleeding = ST14WoundSystem.RawBleed(ent.Comp, args.Zone) > 0.05f;
        _popup.PopupEntity(Loc.GetString(bleeding ? "st14-treat-removed-bleeding" : "st14-treat-removed", ("zone", ST14Zones.Name(args.Zone))),
            ent, args.User, bleeding ? PopupType.MediumCaution : PopupType.Small);

        var strap = Spawn(Tourniquet, Transform(ent).Coordinates);
        _hands.PickupOrDrop(args.User, strap);
    }

    private void CheckPulse(EntityUid user, Entity<ST14WoundsComponent> ent)
    {
        string text;
        if (_mobState.IsDead(ent) || ent.Comp.Heart == ST14HeartState.Arrest)
        {
            text = Loc.GetString("st14-pulse-none");
        }
        else
        {
            var kind = ent.Comp.Heart == ST14HeartState.Arrhythmia ? "erratic"
                : _mobState.IsCritical(ent) ? "thready"
                : ent.Comp.Pulse >= 130 ? "racing"
                : ent.Comp.Pulse >= 100 ? "fast"
                : ent.Comp.Pulse < 60 ? "weak"
                : "steady";
            text = Loc.GetString("st14-pulse-reading", ("pulse", ent.Comp.Pulse), ("kind", Loc.GetString($"st14-pulse-{kind}")));
        }

        _popup.PopupEntity(text, ent, user, PopupType.Medium);
    }

    private void StartCompressions(EntityUid user, EntityUid target)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, 2.5f, new ST14CprDoAfterEvent(), target, target)
        {
            BreakOnMove = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("st14-cpr-start", ("user", user), ("target", target)), target, PopupType.Medium);
    }

    private void OnCompressed(Entity<ST14WoundsComponent> ent, ref ST14CprDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        _audio.PlayPvs(Compression, ent);
        _wounds.Compress(ent);

        // keep pressing while the heart stays still
        args.Repeat = ent.Comp.Heart == ST14HeartState.Arrest && !_mobState.IsDead(ent);
    }
}
