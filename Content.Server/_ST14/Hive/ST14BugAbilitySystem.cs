using System.Numerics;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Combat abilities of the castes. The warrior lives in the .Warrior part, the tiger in the .Tiger part
/// </summary>
public sealed partial class ST14BugAbilitySystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedST14HiveSystem _hive = default!;

    private readonly HashSet<Entity<MobStateComponent>> _victims = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14BugLungeComponent, MapInitEvent>(OnLungeMapInit);
        SubscribeLocalEvent<ST14BugLungeComponent, ComponentShutdown>(OnLungeShutdown);
        SubscribeLocalEvent<ST14BugLungeComponent, ST14BugLungeActionEvent>(OnLunge);
        SubscribeLocalEvent<ST14BugLungingComponent, ThrowDoHitEvent>(OnLungeHit);
        SubscribeLocalEvent<ST14BugLungingComponent, LandEvent>(OnLungeLand);
        SubscribeLocalEvent<ST14BugLungingComponent, StopThrowEvent>(OnLungeStop);

        SubscribeLocalEvent<ST14BugRendComponent, MapInitEvent>(OnRendMapInit);
        SubscribeLocalEvent<ST14BugRendComponent, ComponentShutdown>(OnRendShutdown);
        SubscribeLocalEvent<ST14BugRendComponent, ST14BugRendActionEvent>(OnRend);

        InitializeWarrior();
        InitializeTiger();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateWarrior(frameTime);
        UpdateTiger(frameTime);
    }

    #region Lunge

    private void OnLungeMapInit(Entity<ST14BugLungeComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnLungeShutdown(Entity<ST14BugLungeComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnLunge(Entity<ST14BugLungeComponent> ent, ref ST14BugLungeActionEvent args)
    {
        if (args.Handled)
            return;

        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.ToMapCoordinates(args.Target);
        if (from.MapId != to.MapId)
            return;

        var direction = to.Position - from.Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        if (direction.Length() > ent.Comp.Distance)
            direction = Vector2.Normalize(direction) * ent.Comp.Distance;

        if (!_throwing.TryThrow(ent, direction, ent.Comp.Speed, ent, recoil: false, doSpin: false))
            return;

        args.Handled = true;
        EnsureComp<ST14BugLungingComponent>(ent);

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);
    }

    private void OnLungeHit(Entity<ST14BugLungingComponent> ent, ref ThrowDoHitEvent args)
    {
        var target = args.Target;
        if (!TryComp<ST14BugLungeComponent>(ent, out var lunge) ||
            !HasComp<MobStateComponent>(target) ||
            _hive.IsHive(target) ||
            _mobState.IsDead(target))
            return;

        // one victim per leap
        RemCompDeferred<ST14BugLungingComponent>(ent);

        _damage.TryChangeDamage(target, lunge.Damage, origin: ent);
        _stun.TryKnockdown(target, lunge.Knockdown, force: true);
        _popup.PopupEntity(Loc.GetString("st14-bug-lunge-hit", ("target", target)), target, PopupType.MediumCaution);
    }

    private void OnLungeLand(Entity<ST14BugLungingComponent> ent, ref LandEvent args)
    {
        RemCompDeferred<ST14BugLungingComponent>(ent);
    }

    private void OnLungeStop(Entity<ST14BugLungingComponent> ent, ref StopThrowEvent args)
    {
        RemCompDeferred<ST14BugLungingComponent>(ent);
    }

    #endregion

    #region Rend

    private void OnRendMapInit(Entity<ST14BugRendComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnRendShutdown(Entity<ST14BugRendComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnRend(Entity<ST14BugRendComponent> ent, ref ST14BugRendActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);

        _victims.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _victims);

        var hit = 0;
        foreach (var victim in _victims)
        {
            if (victim.Owner == ent.Owner || _hive.IsHive(victim) || _mobState.IsDead(victim))
                continue;

            _damage.TryChangeDamage(victim.Owner, ent.Comp.Damage * _shared.RageMultiplier(ent), origin: ent);
            _stun.TryAddStunDuration(victim, ent.Comp.Stun);
            hit++;
        }

        _popup.PopupEntity(Loc.GetString(hit > 0 ? "st14-bug-rend-hit" : "st14-bug-rend-miss"), ent, PopupType.Medium);
    }

    #endregion
}
