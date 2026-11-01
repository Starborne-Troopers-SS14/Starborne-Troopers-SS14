using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;

namespace Content.Server._ST14.Hive;

/// <summary>
/// The larva rides into battle on the back of a bigger bug
/// </summary>
public sealed partial class ST14BugAbilitySystem
{
    private readonly List<EntityUid> _riderChecks = new();

    private void InitializeLarva()
    {
        SubscribeLocalEvent<ST14LarvaRideComponent, MapInitEvent>(OnRideMapInit);
        SubscribeLocalEvent<ST14LarvaRideComponent, ComponentShutdown>(OnRideShutdown);
        SubscribeLocalEvent<ST14LarvaRideComponent, ST14LarvaRideActionEvent>(OnRide);
        SubscribeLocalEvent<ST14RidingComponent, BeforeDamageChangedEvent>(OnRiderBeforeDamage);
        SubscribeLocalEvent<ST14CarryingRiderComponent, MobStateChangedEvent>(OnMountMobState);
        SubscribeLocalEvent<ST14CarryingRiderComponent, EntityTerminatingEvent>(OnMountTerminating);
        SubscribeLocalEvent<ST14CarryingRiderComponent, ST14ThrowRiderActionEvent>(OnThrowRider);
    }

    private void UpdateLarva()
    {
        // a rider that is gone, or that evolved into something else, leaves its mount free
        _riderChecks.Clear();
        var query = EntityQueryEnumerator<ST14CarryingRiderComponent>();
        while (query.MoveNext(out var uid, out var carrying))
        {
            if (!TryComp<ST14RidingComponent>(carrying.Rider, out var riding) || riding.Mount != uid)
                _riderChecks.Add(uid);
        }

        foreach (var mount in _riderChecks)
        {
            FreeMount(mount);
        }
    }

    private void OnRideMapInit(Entity<ST14LarvaRideComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnRideShutdown(Entity<ST14LarvaRideComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        Dismount(ent, false);
    }

    private void OnRide(Entity<ST14LarvaRideComponent> ent, ref ST14LarvaRideActionEvent args)
    {
        if (args.Handled)
            return;

        // pressing it again hops off
        if (HasComp<ST14RidingComponent>(ent))
        {
            args.Handled = true;
            Dismount(ent, true);
            return;
        }

        var self = ent.Owner;
        if (PickTarget(args.Target, uid => uid != self && CanCarry(uid)) is not { } mount)
        {
            _popup.PopupEntity(Loc.GetString("st14-larva-ride-invalid"), ent, ent);
            return;
        }

        if (HasComp<ST14CarryingRiderComponent>(mount))
        {
            _popup.PopupEntity(Loc.GetString("st14-larva-ride-taken"), ent, ent);
            return;
        }

        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.GetMapCoordinates(mount);
        if (from.MapId != to.MapId || (to.Position - from.Position).Length() > ent.Comp.Range)
        {
            _popup.PopupEntity(Loc.GetString("st14-bug-leap-too-far"), ent, ent);
            return;
        }

        args.Handled = true;
        Mount(ent, mount);
    }

    /// <summary>
    /// Any live bug of the hive but a larva or a queen can carry a larva
    /// </summary>
    private bool CanCarry(EntityUid uid)
    {
        return HasComp<ST14HiveMemberComponent>(uid) &&
               !HasComp<ST14LarvaRideComponent>(uid) &&
               !HasComp<ST14RidingComponent>(uid) &&
               !_mobState.IsDead(uid);
    }

    private void Mount(Entity<ST14LarvaRideComponent> larva, EntityUid mount)
    {
        if (TryComp<PullableComponent>(larva, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(larva, pullable);

        if (TryComp<PullerComponent>(larva, out var puller) && TryComp<PullableComponent>(puller.Pulling, out var pulled))
            _pulling.TryStopPull(puller.Pulling.Value, pulled);

        var riding = EnsureComp<ST14RidingComponent>(larva);
        riding.Mount = mount;
        Dirty(larva, riding);
        var carrying = EnsureComp<ST14CarryingRiderComponent>(mount);
        carrying.Rider = larva;
        _actions.AddAction(mount, ref carrying.ThrowActionEntity, larva.Comp.ThrowAction);
        Dirty(mount, carrying);

        _physics.SetCanCollide(larva, false);
        _transform.SetCoordinates(larva, new EntityCoordinates(mount, larva.Comp.Offset));
        _transform.SetLocalRotation(larva, Angle.Zero);

        if (larva.Comp.Sound != null)
            _audio.PlayPvs(larva.Comp.Sound, larva);

        _popup.PopupEntity(Loc.GetString("st14-larva-ride-mounted", ("mount", mount)), larva, larva);
        _popup.PopupEntity(Loc.GetString("st14-larva-ride-carrying"), mount, mount);
    }

    /// <summary>
    /// Puts the larva back on the ground next to its mount
    /// </summary>
    private void Dismount(Entity<ST14LarvaRideComponent> larva, bool cooldown)
    {
        if (!TryComp<ST14RidingComponent>(larva, out var riding))
            return;

        var mount = riding.Mount;
        RemComp(larva, riding);

        if (TryComp<ST14CarryingRiderComponent>(mount, out var carrying) && carrying.Rider == larva.Owner)
            FreeMount(mount);

        if (TerminatingOrDeleted(larva))
            return;

        var xform = Transform(larva);
        if (xform.ParentUid == mount)
            _transform.AttachToGridOrMap(larva, xform);

        _physics.SetCanCollide(larva, true);

        if (cooldown)
            StartCooldown(larva.Comp.ActionEntity, larva.Comp.Cooldown);
    }

    /// <summary>
    /// The mount has no larva on its back any more: it loses the throw
    /// </summary>
    private void FreeMount(EntityUid mount)
    {
        if (!TryComp<ST14CarryingRiderComponent>(mount, out var carrying))
            return;

        _actions.RemoveAction(mount, carrying.ThrowActionEntity);
        RemComp(mount, carrying);
    }

    private void OnThrowRider(Entity<ST14CarryingRiderComponent> ent, ref ST14ThrowRiderActionEvent args)
    {
        if (args.Handled || !TryComp<ST14LarvaRideComponent>(ent.Comp.Rider, out var ride))
            return;

        var larva = ent.Comp.Rider;
        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.ToMapCoordinates(args.Target);
        var direction = to.Position - from.Position;
        if (from.MapId != to.MapId || direction.LengthSquared() < 0.01f)
            return;

        if (direction.Length() > ride.ThrowDistance)
            direction = direction.Normalized() * ride.ThrowDistance;

        args.Handled = true;
        Dismount((larva, ride), true);
        _throwing.TryThrow(larva, direction, ride.ThrowSpeed, ent, recoil: false);

        if (ride.Sound != null)
            _audio.PlayPvs(ride.Sound, larva);

        _popup.PopupEntity(Loc.GetString("st14-larva-thrown"), larva, larva, PopupType.Medium);
    }

    private void OnRiderBeforeDamage(Entity<ST14RidingComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // nothing reaches the larva up on the back
        if (args.Damage.AnyPositive())
            args.Cancelled = true;
    }

    private void OnMountMobState(Entity<ST14CarryingRiderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && TryComp<ST14LarvaRideComponent>(ent.Comp.Rider, out var ride))
        {
            Dismount((ent.Comp.Rider, ride), true);
            _popup.PopupEntity(Loc.GetString("st14-larva-ride-fell"), ent.Comp.Rider, PopupType.Medium);
        }
    }

    private void OnMountTerminating(Entity<ST14CarryingRiderComponent> ent, ref EntityTerminatingEvent args)
    {
        // the mount is going away, the larva must not go with it
        if (TryComp<ST14LarvaRideComponent>(ent.Comp.Rider, out var ride))
            Dismount((ent.Comp.Rider, ride), false);
    }
}
