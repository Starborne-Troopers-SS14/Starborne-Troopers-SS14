using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Grip of the hive: the warrior leaps at a trooper, knocks it down and carries it off in its claws to the brain bug.
/// Shots at the trooper hit the bug, the trooper struggles free by pressing the keys shown on its screen
/// </summary>
public sealed partial class ST14BugAbilitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private ST14BrainBugSystem _brain = default!;

    private static readonly MoveButtons[] PromptKeys = { MoveButtons.Up, MoveButtons.Down, MoveButtons.Left, MoveButtons.Right };

    private readonly HashSet<Entity<ST14BrainBugComponent>> _brains = new();
    private readonly List<(EntityUid Victim, EntityUid Grabber)> _gripChecks = new();

    private void InitializeGrip()
    {
        SubscribeLocalEvent<ST14HiveGrabComponent, MapInitEvent>(OnGrabMapInit);
        SubscribeLocalEvent<ST14HiveGrabComponent, ComponentShutdown>(OnGrabShutdown);
        SubscribeLocalEvent<ST14HiveGrabComponent, ST14BugGrabActionEvent>(OnGrab);
        SubscribeLocalEvent<ST14HiveGrabComponent, DamageDealtEvent>(OnGrabberDamaged);
        SubscribeLocalEvent<ST14HiveGrabComponent, MobStateChangedEvent>(OnGrabberMobState);

        SubscribeLocalEvent<ST14HiveGrabLeapComponent, ThrowDoHitEvent>(OnGrabLeapHit);
        SubscribeLocalEvent<ST14HiveGrabLeapComponent, LandEvent>(OnGrabLeapLand);
        SubscribeLocalEvent<ST14HiveGrabLeapComponent, StopThrowEvent>(OnGrabLeapStop);

        SubscribeLocalEvent<ST14HiveGrabbedComponent, MoveInputEvent>(OnGrabbedMoveInput);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, MobStateChangedEvent>(OnGrabbedMobState);
        SubscribeLocalEvent<ST14HiveGrabbedComponent, BeforeDamageChangedEvent>(OnGrabbedBeforeDamage);
    }

    private void UpdateGrip(TimeSpan now, float frameTime)
    {
        _gripChecks.Clear();

        var grabbed = EntityQueryEnumerator<ST14HiveGrabbedComponent>();
        while (grabbed.MoveNext(out var uid, out var comp))
        {
            comp.Struggle = MathF.Max(0f, comp.Struggle - comp.StruggleDecay * frameTime);

            if (now > comp.PromptPerfect + comp.PromptLinger)
                NewPrompt((uid, comp));

            if (now >= comp.NextSync)
            {
                comp.NextSync = now + TimeSpan.FromSeconds(0.25);
                Dirty(uid, comp);
            }

            _gripChecks.Add((uid, comp.Grabber));
        }

        foreach (var (victim, grabber) in _gripChecks)
        {
            if (!TryComp<ST14HiveGrabComponent>(grabber, out var grab) || grab.Victim != victim)
            {
                Unhook(victim);
                continue;
            }

            TryDeliver((grabber, grab), victim);
        }
    }

    private void OnGrabMapInit(Entity<ST14HiveGrabComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnGrabShutdown(Entity<ST14HiveGrabComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        Release(ent, null);
    }

    private void OnGrab(Entity<ST14HiveGrabComponent> ent, ref ST14BugGrabActionEvent args)
    {
        if (args.Handled)
            return;

        // pressing it again opens the claws
        if (ent.Comp.Victim != null)
        {
            args.Handled = true;
            Release(ent, "st14-bug-grab-let-go");
            return;
        }

        var target = args.Target;
        if (!CanGrab(target))
        {
            _popup.PopupEntity(Loc.GetString("st14-bug-grab-invalid"), ent, ent);
            return;
        }

        if (HasComp<ST14InBarricadeComponent>(ent))
        {
            _popup.PopupEntity(Loc.GetString("st14-bug-grab-in-barricade"), ent, ent);
            return;
        }

        args.Handled = true;
        EnsureComp<ST14HiveGrabLeapComponent>(ent).Target = target;

        if (ent.Comp.LeapSound != null)
            _audio.PlayPvs(ent.Comp.LeapSound, ent);

        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.GetMapCoordinates(target);
        var direction = to.Position - from.Position;

        // right next to the trooper there is nothing to leap over
        if (from.MapId != to.MapId ||
            direction.Length() <= ent.Comp.CatchRange ||
            !_throwing.TryThrow(ent, direction, ent.Comp.Speed, ent, recoil: false, doSpin: false))
        {
            Catch(ent);
        }
    }

    private bool CanGrab(EntityUid target)
    {
        return HasComp<MobStateComponent>(target) &&
               !_hive.IsHive(target) &&
               !_mobState.IsDead(target) &&
               !HasComp<ST14HiveGrabbedComponent>(target);
    }

    private void OnGrabLeapHit(Entity<ST14HiveGrabLeapComponent> ent, ref ThrowDoHitEvent args)
    {
        if (args.Target == ent.Comp.Target)
            Catch(ent);
    }

    private void OnGrabLeapLand(Entity<ST14HiveGrabLeapComponent> ent, ref LandEvent args)
    {
        Catch(ent);
    }

    private void OnGrabLeapStop(Entity<ST14HiveGrabLeapComponent> ent, ref StopThrowEvent args)
    {
        Catch(ent);
    }

    /// <summary>
    /// The end of the leap: a trooper still in reach is knocked down and taken into the claws
    /// </summary>
    private void Catch(EntityUid bug)
    {
        if (!TryComp<ST14HiveGrabLeapComponent>(bug, out var leap) || !TryComp<ST14HiveGrabComponent>(bug, out var grab))
            return;

        var target = leap.Target;
        RemComp(bug, leap);

        if (TerminatingOrDeleted(target) || _mobState.IsDead(bug) || grab.Victim != null)
            return;

        var from = _transform.GetMapCoordinates(bug);
        var to = _transform.GetMapCoordinates(target);
        if (!CanGrab(target) || from.MapId != to.MapId || (to.Position - from.Position).Length() > grab.CatchRange)
        {
            _actions.SetCooldown(grab.ActionEntity, grab.MissCooldown);
            _popup.PopupEntity(Loc.GetString("st14-bug-grab-miss"), bug, bug);
            return;
        }

        Hook((bug, grab), target);
    }

    /// <summary>
    /// Takes the trooper into the claws: it lies in front of the bug and goes wherever the bug goes
    /// </summary>
    private void Hook(Entity<ST14HiveGrabComponent> bug, EntityUid victim)
    {
        // nobody else keeps dragging it, and it lets go of whatever it dragged
        if (TryComp<PullableComponent>(victim, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(victim, pullable);

        if (TryComp<PullerComponent>(victim, out var puller) && TryComp<PullableComponent>(puller.Pulling, out var pulled))
            _pulling.TryStopPull(puller.Pulling.Value, pulled);

        if (TryComp<PullerComponent>(bug, out var bugPuller) && TryComp<PullableComponent>(bugPuller.Pulling, out var bugPulled))
            _pulling.TryStopPull(bugPuller.Pulling.Value, bugPulled);

        bug.Comp.Victim = victim;
        bug.Comp.DamageTaken = 0;

        var grabbed = EnsureComp<ST14HiveGrabbedComponent>(victim);
        grabbed.Grabber = bug;
        grabbed.Struggle = 0;
        NewPrompt((victim, grabbed));

        _standing.Down(victim, dropHeldItems: false);
        _physics.SetCanCollide(victim, false);
        _transform.SetCoordinates(victim, new EntityCoordinates(bug, bug.Comp.HoldOffset));
        _transform.SetLocalRotation(victim, Angle.Zero);

        if (bug.Comp.Sound != null)
            _audio.PlayPvs(bug.Comp.Sound, bug);

        _popup.PopupEntity(Loc.GetString("st14-bug-grab-victim"), victim, victim, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("st14-bug-grab-start", ("target", victim)), bug, Filter.PvsExcept(victim), true, PopupType.MediumCaution);
    }

    /// <summary>
    /// Opens the claws and drops the trooper in front of the bug. The reason is shown to everyone around, null for a silent release
    /// </summary>
    private void Release(Entity<ST14HiveGrabComponent> ent, string? reason)
    {
        if (ent.Comp.Victim is not { } victim)
            return;

        ent.Comp.Victim = null;
        _actions.SetCooldown(ent.Comp.ActionEntity, ent.Comp.Cooldown);

        if (TerminatingOrDeleted(victim))
            return;

        Unhook(victim);

        if (!_mobState.IsDead(victim))
            _stun.TryKnockdown(victim, ent.Comp.DropKnockdown, force: true);

        if (reason == null)
            return;

        if (ent.Comp.ReleaseSound != null)
            _audio.PlayPvs(ent.Comp.ReleaseSound, ent);

        _popup.PopupEntity(Loc.GetString(reason, ("bug", ent.Owner), ("target", victim)), victim, PopupType.Medium);
    }

    /// <summary>
    /// Puts the trooper back on the ground on its own
    /// </summary>
    private void Unhook(EntityUid victim)
    {
        if (!TryComp<ST14HiveGrabbedComponent>(victim, out var grabbed))
            return;

        var grabber = grabbed.Grabber;
        RemComp(victim, grabbed);

        var xform = Transform(victim);
        if (xform.ParentUid == grabber)
            _transform.AttachToGridOrMap(victim, xform);

        _physics.SetCanCollide(victim, true);
    }

    private void OnGrabberDamaged(Entity<ST14HiveGrabComponent> ent, ref DamageDealtEvent args)
    {
        if (ent.Comp.Victim == null)
            return;

        foreach (var value in args.Damage.DamageDict.Values)
        {
            if (value > 0)
                ent.Comp.DamageTaken += value;
        }

        if (ent.Comp.DamageTaken >= ent.Comp.BreakDamage)
            Release(ent, "st14-bug-grab-shot-off");
    }

    private void OnGrabberMobState(Entity<ST14HiveGrabComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Release(ent, "st14-bug-grab-let-go");
    }

    private void OnGrabbedMobState(Entity<ST14HiveGrabbedComponent> ent, ref MobStateChangedEvent args)
    {
        // a corpse is no use to the brain bug, the claws drop it
        if (args.NewMobState == MobState.Dead && TryComp<ST14HiveGrabComponent>(ent.Comp.Grabber, out var grab))
            Release((ent.Comp.Grabber, grab), null);
    }

    private void OnGrabbedBeforeDamage(Entity<ST14HiveGrabbedComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // bleeding and other harm with no one behind it still hurts the trooper
        if (args.Cancelled || args.Origin is not { } origin || !args.Damage.AnyPositive())
            return;

        args.Cancelled = true;

        // the claws cover the trooper: whatever is aimed at it hits the bug, and the hive never hurts its own catch
        if (!_hive.IsHive(origin) && !TerminatingOrDeleted(ent.Comp.Grabber))
            _damage.TryChangeDamage(ent.Comp.Grabber, args.Damage, origin: origin);
    }

    #region Struggle

    private void NewPrompt(Entity<ST14HiveGrabbedComponent> ent)
    {
        var old = ent.Comp.Prompt;
        while (ent.Comp.Prompt == old)
        {
            ent.Comp.Prompt = _random.Pick(PromptKeys);
        }

        ent.Comp.PromptPerfect = _timing.CurTime + ent.Comp.PromptLead;
        Dirty(ent);
    }

    private void OnGrabbedMoveInput(Entity<ST14HiveGrabbedComponent> ent, ref MoveInputEvent args)
    {
        // only a freshly pressed key is a jerk against the claws
        var pressed = args.Entity.Comp.HeldMoveButtons & ~args.OldMovement & MoveButtons.AnyDirection;
        if (pressed == MoveButtons.None || _mobState.IsIncapacitated(ent))
            return;

        if ((pressed & ent.Comp.Prompt) != MoveButtons.None)
        {
            var off = _timing.CurTime - ent.Comp.PromptPerfect;
            if (off.Duration() <= ent.Comp.PerfectWindow)
            {
                ent.Comp.Struggle += ent.Comp.PerfectGain;
                _popup.PopupEntity(Loc.GetString("st14-bug-grab-perfect"), ent, ent, PopupType.Medium);
            }
            else
            {
                ent.Comp.Struggle += ent.Comp.HitGain;
            }

            NewPrompt(ent);
        }
        else
        {
            ent.Comp.Struggle += ent.Comp.MashGain;
            Dirty(ent);
        }

        if (ent.Comp.Struggle < ent.Comp.StruggleNeeded)
            return;

        if (TryComp<ST14HiveGrabComponent>(ent.Comp.Grabber, out var grab))
            Release((ent.Comp.Grabber, grab), "st14-bug-grab-broke-free");
    }

    #endregion

    /// <summary>
    /// A trooper carried up to the brain bug gets its skull pierced right away
    /// </summary>
    private void TryDeliver(Entity<ST14HiveGrabComponent> grabber, EntityUid victim)
    {
        _brains.Clear();
        _lookup.GetEntitiesInRange(Transform(victim).Coordinates, grabber.Comp.DeliveryRange, _brains);

        foreach (var brain in _brains)
        {
            if (brain.Comp.Sucking)
                return;

            if (_brain.TryStartSuck(brain, victim, false))
                return;
        }
    }
}
