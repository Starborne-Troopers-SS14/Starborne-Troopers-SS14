using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Electrocution;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Medical;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Timing.Components;
using Content.Shared.Timing.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// The charge of <see cref="ST14FieldDefibComponent"/>. The shock itself is the stock one of the defibrillator
/// </summary>
public sealed partial class ST14FieldDefibSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedChatSystem _chat = default!;
    [Dependency] private SharedDefibrillatorSystem _defib = default!;
    [Dependency] private SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14FieldDefibComponent, AfterInteractEvent>(OnAfterInteract);
    }

    /// <summary>
    /// How full the charge is, 1 is an overload
    /// </summary>
    public float Charge(ST14FieldDefibComponent comp)
    {
        return (float) (_timing.CurTime - comp.ChargeStart).TotalSeconds / comp.ChargeTime;
    }

    private void OnAfterInteract(Entity<ST14FieldDefibComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach || !HasComp<MobStateComponent>(target))
            return;

        args.Handled = true;

        if (!_defib.CanZap(ent.Owner, target, args.User))
            return;

        if (!HasComp<HumanoidProfileComponent>(target))
        {
            Poke(ent, target);
            return;
        }

        if (ent.Comp.Target == null)
            StartCharge(ent, target, args.User);
        else if (ent.Comp.Target == target)
            Discharge(ent, target, args.User);
        else
            StopCharge(ent);
    }

    private void StartCharge(Entity<ST14FieldDefibComponent> ent, EntityUid target, EntityUid user)
    {
        if (!_mobState.IsIncapacitated(target))
        {
            _popup.PopupClient(Loc.GetString("st14-field-defib-not-needed"), target, user);
            return;
        }

        ent.Comp.User = user;
        ent.Comp.Target = target;
        ent.Comp.ChargeStart = _timing.CurTime;
        ent.Comp.UserDamage = _damageable.GetTotalDamage(user);
        Dirty(ent);

        if (TryComp<DefibrillatorComponent>(ent, out var defib))
            _audio.PlayPredicted(defib.ChargeSound, ent.Owner, user);

        _popup.PopupClient(Loc.GetString("st14-field-defib-charging"), user, user);
    }

    private void StopCharge(Entity<ST14FieldDefibComponent> ent)
    {
        ent.Comp.User = null;
        ent.Comp.Target = null;
        Dirty(ent);
    }

    private void Discharge(Entity<ST14FieldDefibComponent> ent, EntityUid target, EntityUid user)
    {
        var charge = Charge(ent.Comp);
        StopCharge(ent);

        if (charge >= ent.Comp.GreenStart)
            _damageable.TryChangeDamage(target, ent.Comp.GoodHeal, true, false, user);

        if (charge > ent.Comp.GreenEnd)
            _electrocution.TryDoElectrocution(target, ent.Owner, ent.Comp.HotShock, TimeSpan.Zero, true, ignoreInsulation: true);

        _chat.TrySendInGameICMessage(user, Loc.GetString("st14-field-defib-clear"), InGameICChatType.Speak, false);
        _defib.Zap(ent.Owner, target, user);
    }

    /// <summary>
    /// Pads on a bug: a short stun, and the defibrillator goes on its cooldown
    /// </summary>
    private void Poke(Entity<ST14FieldDefibComponent> ent, EntityUid target)
    {
        _electrocution.TryDoElectrocution(target, ent.Owner, ent.Comp.PokeShock, ent.Comp.PokeStun, true, ignoreInsulation: true);
        StartCooldown(ent);
    }

    private void Overload(Entity<ST14FieldDefibComponent> ent, EntityUid target, EntityUid user)
    {
        StopCharge(ent);

        _electrocution.TryDoElectrocution(target, ent.Owner, ent.Comp.OverloadShock, TimeSpan.FromSeconds(3), true, ignoreInsulation: true);
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(Transform(target).Coordinates, ent.Comp.OverloadRange))
        {
            if (mob.Owner != target)
                _electrocution.TryDoElectrocution(mob, ent.Owner, ent.Comp.OverloadSplashShock, TimeSpan.FromSeconds(1), true);
        }

        if (TryComp<DefibrillatorComponent>(ent, out var defib))
            _audio.PlayPvs(defib.FailureSound, ent.Owner);

        _popup.PopupEntity(Loc.GetString("st14-field-defib-overload"), target, PopupType.LargeCaution);
        StartCooldown(ent);
    }

    private void StartCooldown(Entity<ST14FieldDefibComponent> ent)
    {
        if (!TryComp<DefibrillatorComponent>(ent, out var defib) || !TryComp<UseDelayComponent>(ent, out var delay))
            return;

        _useDelay.SetLength((ent.Owner, delay), defib.ZapDelay, id: defib.DelayId);
        _useDelay.TryResetDelay((ent.Owner, delay), id: defib.DelayId);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // the end of a charge is decided by the server alone, the client sees it in the state
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<ST14FieldDefibComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Target is not { } target || comp.User is not { } user)
                continue;

            if (TerminatingOrDeleted(target) ||
                TerminatingOrDeleted(user) ||
                Transform(uid).ParentUid != user ||
                !_interaction.InRangeUnobstructed(user, target, comp.OverloadRange) ||
                _damageable.GetTotalDamage(user) > comp.UserDamage)
            {
                StopCharge((uid, comp));
                _popup.PopupEntity(Loc.GetString("st14-field-defib-broken"), user, user);
                continue;
            }

            if (Charge(comp) >= 1f)
                Overload((uid, comp), target, user);
        }
    }
}
