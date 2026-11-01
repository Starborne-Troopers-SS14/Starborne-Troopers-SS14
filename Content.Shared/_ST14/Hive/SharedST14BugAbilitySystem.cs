using Content.Shared._ST14.Hive.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Pulling.Events;
using Content.Shared.Standing;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._ST14.Hive;

/// <summary>
/// Predicted side of the caste abilities: what resonance, a grip, a living barricade and the last rush
/// change about moving, shooting and hitting
/// </summary>
public sealed partial class SharedST14BugAbilitySystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14SwarmResonanceComponent, RefreshMovementSpeedModifiersEvent>(OnResonanceSpeed);
        SubscribeLocalEvent<ST14SwarmResonanceComponent, GetMeleeDamageEvent>(OnResonanceDamage);
        SubscribeLocalEvent<ST14SwarmResonanceComponent, AfterAutoHandleStateEvent>(OnResonanceState);

        SubscribeLocalEvent<ST14HiveGrabbedComponent, UpdateCanMoveEvent>(OnGrabbedCanMove);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, ShotAttemptedEvent>(OnGrabbedShot);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, AttackAttemptEvent>(OnGrabbedAttack);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, StandAttemptEvent>(OnGrabbedStand);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, BeingPulledAttemptEvent>(OnGrabbedPullAttempt);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, ComponentStartup>(OnBlockMoveChanged);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, ComponentShutdown>(OnBlockMoveChanged);

        SubscribeLocalEvent<ST14RidingComponent, UpdateCanMoveEvent>(OnRidingCanMove);
        SubscribeLocalEvent<ST14RidingComponent, ComponentStartup>(OnBlockMoveChanged);
        SubscribeLocalEvent<ST14RidingComponent, ComponentShutdown>(OnBlockMoveChanged);

        SubscribeLocalEvent<ST14SlimedComponent, UpdateCanMoveEvent>(OnSlimedCanMove);
        SubscribeLocalEvent<ST14SlimedComponent, ShotAttemptedEvent>(OnSlimedShot);
        SubscribeLocalEvent<ST14SlimedComponent, AttackAttemptEvent>(OnSlimedAttack);
        SubscribeLocalEvent<ST14SlimedComponent, AfterAutoHandleStateEvent>(OnSlimedState);
        SubscribeLocalEvent<ST14SlimedComponent, ComponentShutdown>(OnBlockMoveChanged);

        SubscribeLocalEvent<ST14InBarricadeComponent, UpdateCanMoveEvent>(OnBarricadeCanMove);
        SubscribeLocalEvent<ST14InBarricadeComponent, ComponentStartup>(OnBlockMoveChanged);
        SubscribeLocalEvent<ST14InBarricadeComponent, ComponentShutdown>(OnBlockMoveChanged);

        SubscribeLocalEvent<ST14LastRushComponent, BeforeDamageChangedEvent>(OnLastRushBeforeDamage);
        SubscribeLocalEvent<ST14LastRushComponent, GetMeleeDamageEvent>(OnLastRushDamage);
    }

    #region Resonance

    private void OnResonanceSpeed(Entity<ST14SwarmResonanceComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Allies > 0)
            args.ModifySpeed(1f + ent.Comp.SpeedPerAlly * ent.Comp.Allies);
    }

    private void OnResonanceDamage(Entity<ST14SwarmResonanceComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User == ent.Owner && ent.Comp.Allies > 0)
            args.Damage *= 1f + ent.Comp.DamagePerAlly * ent.Comp.Allies;
    }

    private void OnResonanceState(Entity<ST14SwarmResonanceComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    /// <summary>
    /// Sets how many resonating bugs stand around this one
    /// </summary>
    public void SetResonance(Entity<ST14SwarmResonanceComponent> ent, int allies)
    {
        allies = Math.Clamp(allies, 0, ent.Comp.MaxAllies);
        if (ent.Comp.Allies == allies)
            return;

        ent.Comp.Allies = allies;
        Dirty(ent);
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    #endregion

    #region Grip

    private void OnGrabbedCanMove(Entity<ST14HiveGrabbedComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void OnGrabbedShot(Entity<ST14HiveGrabbedComponent> ent, ref ShotAttemptedEvent args)
    {
        args.Cancel();
    }

    private void OnGrabbedAttack(Entity<ST14HiveGrabbedComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }

    private void OnGrabbedStand(Entity<ST14HiveGrabbedComponent> ent, ref StandAttemptEvent args)
    {
        // a trooper in the claws hangs there, it does not get up
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void OnGrabbedPullAttempt(Entity<ST14HiveGrabbedComponent> ent, ref BeingPulledAttemptEvent args)
    {
        args.Cancel();
    }

    #endregion

    #region Riding and slime

    private void OnRidingCanMove(Entity<ST14RidingComponent> ent, ref UpdateCanMoveEvent args)
    {
        // the mount does the walking
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void OnSlimedCanMove(Entity<ST14SlimedComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.Cocooned && ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    private void OnSlimedShot(Entity<ST14SlimedComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ent.Comp.Cocooned)
            args.Cancel();
    }

    private void OnSlimedAttack(Entity<ST14SlimedComponent> ent, ref AttackAttemptEvent args)
    {
        if (ent.Comp.Cocooned)
            args.Cancel();
    }

    private void OnSlimedState(Entity<ST14SlimedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _blocker.UpdateCanMove(ent);
    }

    /// <summary>
    /// Wraps the trooper in a cocoon or lets it out
    /// </summary>
    public void SetCocooned(Entity<ST14SlimedComponent> ent, bool cocooned)
    {
        ent.Comp.Cocooned = cocooned;
        Dirty(ent);
        _blocker.UpdateCanMove(ent);
    }

    #endregion

    #region Barricade

    private void OnBarricadeCanMove(Entity<ST14InBarricadeComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }

    #endregion

    private void OnBlockMoveChanged<T>(Entity<T> ent, ref ComponentStartup args) where T : IComponent
    {
        _blocker.UpdateCanMove(ent);
    }

    private void OnBlockMoveChanged<T>(Entity<T> ent, ref ComponentShutdown args) where T : IComponent
    {
        _blocker.UpdateCanMove(ent);
    }

    #region Last rush

    private void OnLastRushBeforeDamage(Entity<ST14LastRushComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // healing always goes through
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;

        // untouchable while raging
        if (ent.Comp.Active)
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.Used || _net.IsClient || _mobState.IsDead(ent) ||
            !_thresholds.TryGetThresholdForState(ent, MobState.Dead, out var dead))
            return;

        var incoming = DamageSpecifier.GetPositive(args.Damage);
        if (_damage.GetDamageModifierSet(ent.Owner) is { } modifiers)
            incoming = DamageSpecifier.ApplyModifierSet(incoming, modifiers);

        if (_damage.GetTotalDamage(ent.Owner) + incoming.GetTotal() < dead)
            return;

        // the killing blow is shrugged off, the bug rages instead
        args.Cancelled = true;
        ent.Comp.Used = true;
        ent.Comp.Active = true;
        ent.Comp.End = _timing.CurTime + ent.Comp.Duration;
        Dirty(ent);

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);

        _popup.PopupEntity(Loc.GetString("st14-bug-last-rush"), ent, PopupType.LargeCaution);
    }

    /// <summary>
    /// How much harder the bug hits right now, twice as hard while it is in its last rush
    /// </summary>
    public float RageMultiplier(EntityUid uid)
    {
        return TryComp<ST14LastRushComponent>(uid, out var rush) && rush.Active ? rush.DamageMultiplier : 1f;
    }

    private void OnLastRushDamage(Entity<ST14LastRushComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User == ent.Owner && ent.Comp.Active)
            args.Damage *= ent.Comp.DamageMultiplier;
    }

    #endregion
}
