using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._ST14.Medicine;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Content.Shared.Traits.Assorted;
using Robust.Server.Audio;
using Robust.Shared.Containers;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// Fits a prosthesis to a stump: the arm with its hand or the leg with its foot go into the body and work like flesh.
/// Without morphine the patient screams through it
/// </summary>
public sealed partial class ST14ProsthesisSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private OrganRelationSystem _relation = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private ST14MissingLegsSystem _legs = default!;
    [Dependency] private ST14WoundSystem _wounds = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ST14ProsthesisItemComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ST14ProsthesisItemComponent, ST14FitProsthesisDoAfterEvent>(OnFitted);
    }

    /// <summary>
    /// The side the limb is missing on, the left one first. Null when the body has both
    /// </summary>
    private bool? MissingSide(EntityUid body, ST14ProsthesisLimb limb)
    {
        var (left, right) = limb == ST14ProsthesisLimb.Arm ? ("ArmLeft", "ArmRight") : ("LegLeft", "LegRight");
        var categories = _body.EnumerateOrgans<OrganComponent>(body).Select(o => o.Comp1.Category?.Id).ToHashSet();
        if (!categories.Contains(left))
            return true;

        if (!categories.Contains(right))
            return false;

        return null;
    }

    private bool Numb(EntityUid uid)
    {
        return _wounds.IsNumb(uid);
    }

    private static ST14BodyZone ZoneOf(ST14ProsthesisLimb limb, bool left)
    {
        return limb == ST14ProsthesisLimb.Arm
            ? left ? ST14BodyZone.ArmLeft : ST14BodyZone.ArmRight
            : left ? ST14BodyZone.LegLeft : ST14BodyZone.LegRight;
    }

    private void OnAfterInteract(Entity<ST14ProsthesisItemComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<HumanoidProfileComponent>(target) || !HasComp<BodyComponent>(target))
            return;

        args.Handled = true;
        if (MissingSide(target, ent.Comp.Limb) is not { } side)
        {
            _popup.PopupEntity(Loc.GetString("st14-prosthesis-not-needed", ("target", target)), target, args.User);
            return;
        }

        // a torn stump has to be sewn shut before anything is fitted to it
        if (_wounds.HasStump(target, ZoneOf(ent.Comp.Limb, side), out var sutured) && !sutured)
        {
            _popup.PopupEntity(Loc.GetString("st14-prosthesis-suture-first"), target, args.User, PopupType.MediumCaution);
            return;
        }

        if (ent.Comp.NeedsLying && !_standing.IsDown(target))
        {
            _popup.PopupEntity(Loc.GetString("st14-prosthesis-lie-down", ("target", target)), target, args.User, PopupType.MediumCaution);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay, new ST14FitProsthesisDoAfterEvent(), ent, target, ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _popup.PopupEntity(Loc.GetString("st14-prosthesis-start", ("user", args.User), ("target", target)), target, PopupType.Medium);
        if (!Numb(target) && !_mobState.IsIncapacitated(target))
        {
            _chat.TryEmoteWithChat(target, "Scream", ignoreActionBlocker: true, forceEmote: true);
            _jitter.DoJitter(target, ent.Comp.Delay, true, 12f, 7f);
            _popup.PopupEntity(Loc.GetString("st14-prosthesis-pain"), target, target, PopupType.LargeCaution);
            _wounds.AddPain(target, 45f);
        }
    }

    private void OnFitted(Entity<ST14ProsthesisItemComponent> ent, ref ST14FitProsthesisDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = true;
        Fit(ent, target);
    }

    /// <summary>
    /// Puts the prosthesis into the body on the side the limb is missing and uses the item up
    /// </summary>
    public bool Fit(Entity<ST14ProsthesisItemComponent> ent, EntityUid target)
    {
        if (MissingSide(target, ent.Comp.Limb) is not { } left ||
            !_container.TryGetContainer(target, BodyComponent.ContainerID, out var container) ||
            _body.EnumerateOrgans<ParentOrganComponent>(target).Where(o => o.Comp1.Category?.Id == "Torso").Select(o => (EntityUid?) o.Owner).FirstOrDefault() is not { } torso)
        {
            return false;
        }

        var coordinates = Transform(target).Coordinates;
        var limb = Spawn(left ? ent.Comp.LeftLimb : ent.Comp.RightLimb, coordinates);
        var end = Spawn(left ? ent.Comp.LeftEnd : ent.Comp.RightEnd, coordinates);
        if (!_container.Insert(limb, container, force: true) || !_container.Insert(end, container, force: true))
        {
            Del(limb);
            Del(end);
            return false;
        }

        _relation.Relate(torso, limb);
        _relation.Relate(limb, end);

        // the prosthesis takes the place of the stump
        _wounds.LimbRestored(target, ZoneOf(ent.Comp.Limb, left));

        _legs.Refresh(target);

        if (ent.Comp.FitSound != null)
            _audio.PlayPvs(ent.Comp.FitSound, target);

        _popup.PopupEntity(Loc.GetString("st14-prosthesis-done", ("target", target)), target, PopupType.Medium);
        QueueDel(ent);
        return true;
    }
}
