using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Powers of the brain bug: slime spit that cocoons a trooper, and a scream that fills heads with visions and ringing
/// </summary>
public sealed partial class ST14BugAbilitySystem
{
    [Dependency] private GunSystem _gun = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private readonly List<EntityUid> _slimeChecks = new();

    private void InitializeBrain()
    {
        SubscribeLocalEvent<ST14BrainSpitComponent, MapInitEvent>(OnSpitMapInit);
        SubscribeLocalEvent<ST14BrainSpitComponent, ComponentShutdown>(OnSpitShutdown);
        SubscribeLocalEvent<ST14BrainSpitComponent, ST14BrainSpitActionEvent>(OnSpit);
        SubscribeLocalEvent<ST14SlimeSpitComponent, PreventCollideEvent>(OnSpitPreventCollide);
        SubscribeLocalEvent<ST14SlimeSpitComponent, ProjectileHitEvent>(OnSpitHit);

        SubscribeLocalEvent<ST14BrainScreamComponent, MapInitEvent>(OnScreamMapInit);
        SubscribeLocalEvent<ST14BrainScreamComponent, ComponentShutdown>(OnScreamShutdown);
        SubscribeLocalEvent<ST14BrainScreamComponent, ST14BrainScreamActionEvent>(OnScream);
    }

    private void UpdateBrain(TimeSpan now)
    {
        _slimeChecks.Clear();
        var slimed = EntityQueryEnumerator<ST14SlimedComponent>();
        while (slimed.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.End)
                _slimeChecks.Add(uid);
        }

        foreach (var uid in _slimeChecks)
        {
            if (TryComp<ST14SlimedComponent>(uid, out var comp) && comp.Cocooned)
                _popup.PopupEntity(Loc.GetString("st14-brain-spit-free", ("target", uid)), uid, PopupType.Medium);

            RemComp<ST14SlimedComponent>(uid);
        }

        _slimeChecks.Clear();
        var deaf = EntityQueryEnumerator<ST14DeafenedComponent>();
        while (deaf.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.End)
                _slimeChecks.Add(uid);
        }

        foreach (var uid in _slimeChecks)
        {
            RemComp<ST14DeafenedComponent>(uid);
        }
    }

    #region Spit

    private void OnSpitMapInit(Entity<ST14BrainSpitComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnSpitShutdown(Entity<ST14BrainSpitComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnSpit(Entity<ST14BrainSpitComponent> ent, ref ST14BrainSpitActionEvent args)
    {
        if (args.Handled)
            return;

        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.ToMapCoordinates(args.Target);
        var direction = to.Position - from.Position;
        if (from.MapId != to.MapId || direction.LengthSquared() < 0.01f)
            return;

        args.Handled = true;

        var spit = Spawn(ent.Comp.Projectile, from);
        _gun.ShootProjectile(spit, direction, _physics.GetMapLinearVelocity(ent), ent, ent, ent.Comp.Speed);

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    private void OnSpitPreventCollide(Entity<ST14SlimeSpitComponent> ent, ref PreventCollideEvent args)
    {
        // the slime flies over the hive and only sticks to troopers
        if (_hive.IsHive(args.OtherEntity))
            args.Cancelled = true;
    }

    private void OnSpitHit(Entity<ST14SlimeSpitComponent> ent, ref ProjectileHitEvent args)
    {
        var target = args.Target;
        if (!HasComp<MobStateComponent>(target) || _hive.IsHive(target) || _mobState.IsDead(target))
            return;

        var slimed = EnsureComp<ST14SlimedComponent>(target);
        if (slimed.Cocooned)
            return;

        var now = _timing.CurTime;
        slimed.Globs++;
        slimed.End = now + slimed.GlobLifetime;

        if (slimed.Globs < slimed.GlobsNeeded)
        {
            Dirty(target, slimed);
            _popup.PopupEntity(Loc.GetString("st14-brain-spit-glob", ("globs", slimed.Globs), ("needed", slimed.GlobsNeeded)), target, target, PopupType.SmallCaution);
            return;
        }

        slimed.Globs = 0;
        slimed.End = now + slimed.CocoonTime;
        _shared.SetCocooned((target, slimed), true);
        _popup.PopupEntity(Loc.GetString("st14-brain-spit-cocoon", ("target", target)), target, PopupType.LargeCaution);
    }

    #endregion

    #region Scream

    private void OnScreamMapInit(Entity<ST14BrainScreamComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnScreamShutdown(Entity<ST14BrainScreamComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnScream(Entity<ST14BrainScreamComponent> ent, ref ST14BrainScreamActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);

        _popup.PopupEntity(Loc.GetString("st14-brain-scream"), ent, PopupType.LargeCaution);

        var end = _timing.CurTime + ent.Comp.Deafness;
        _victims.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _victims);
        foreach (var victim in _victims)
        {
            if (_hive.IsHive(victim) || _mobState.IsDead(victim))
                continue;

            _statusEffects.TryUpdateStatusEffectDuration(victim, ent.Comp.HallucinationEffect, ent.Comp.Hallucinations);
            EnsureComp<ST14DeafenedComponent>(victim).End = end;

            if (ent.Comp.RingingSound != null)
                _audio.PlayEntity(ent.Comp.RingingSound, Filter.Entities(victim.Owner), victim, false);

            _popup.PopupEntity(Loc.GetString("st14-brain-scream-victim"), victim, victim, PopupType.LargeCaution);
        }
    }

    #endregion
}
