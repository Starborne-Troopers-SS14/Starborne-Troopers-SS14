using System.Linq;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Hive;

/// <summary>
/// The warrior: swarm resonance and the living barricade. The grip lives in the .Grip part
/// </summary>
public sealed partial class ST14BugAbilitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedST14BugAbilitySystem _shared = default!;

    private readonly HashSet<Entity<ST14SwarmResonanceComponent>> _resonating = new();
    private readonly HashSet<Entity<ST14LivingBarricadeComponent>> _barricadeCandidates = new();
    private readonly List<List<EntityUid>> _expiredWalls = new();

    // Damage a barricade spreads over its bugs is dealt again to each of them, this keeps it from spreading twice
    private bool _sharingDamage;

    private void InitializeWarrior()
    {
        SubscribeLocalEvent<ST14SwarmResonanceComponent, MobStateChangedEvent>(OnResonanceMobState);

        InitializeGrip();

        SubscribeLocalEvent<ST14LivingBarricadeComponent, MapInitEvent>(OnBarricadeMapInit);
        SubscribeLocalEvent<ST14LivingBarricadeComponent, ComponentShutdown>(OnBarricadeShutdown);
        SubscribeLocalEvent<ST14LivingBarricadeComponent, ST14BugBarricadeActionEvent>(OnBarricade);
        SubscribeLocalEvent<ST14InBarricadeComponent, BeforeDamageChangedEvent>(OnBarricadeBeforeDamage);
        SubscribeLocalEvent<ST14InBarricadeComponent, MobStateChangedEvent>(OnBarricadeMobState);
        SubscribeLocalEvent<ST14BarricadeShieldComponent, BeforeDamageChangedEvent>(OnShieldBeforeDamage);
    }

    private void UpdateWarrior(float frameTime)
    {
        var now = _timing.CurTime;

        var resonance = EntityQueryEnumerator<ST14SwarmResonanceComponent>();
        while (resonance.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + comp.UpdateInterval;
            UpdateResonance((uid, comp));
        }

        UpdateGrip(now, frameTime);

        // a shield whose bug is gone or left the wall goes with it
        var shields = EntityQueryEnumerator<ST14BarricadeShieldComponent>();
        while (shields.MoveNext(out var uid, out var comp))
        {
            if (!TryComp<ST14InBarricadeComponent>(comp.Bug, out var bugWall) || bugWall.Shield != uid)
                QueueDel(uid);
        }

        _expiredWalls.Clear();
        var barricades = EntityQueryEnumerator<ST14InBarricadeComponent>();
        while (barricades.MoveNext(out _, out var comp))
        {
            if (now >= comp.End)
                _expiredWalls.Add(comp.Members.ToList());
        }

        foreach (var wall in _expiredWalls)
        {
            BreakBarricade(wall);
        }
    }

    #region Resonance

    private void UpdateResonance(Entity<ST14SwarmResonanceComponent> ent)
    {
        var allies = 0;
        if (!_mobState.IsDead(ent))
        {
            _resonating.Clear();
            _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _resonating);

            foreach (var other in _resonating)
            {
                if (other.Owner != ent.Owner && !_mobState.IsDead(other))
                    allies++;
            }
        }

        _shared.SetResonance(ent, allies);
    }

    private void OnResonanceMobState(Entity<ST14SwarmResonanceComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            _shared.SetResonance(ent, 0);
    }

    #endregion

    #region Barricade

    private void OnBarricadeMapInit(Entity<ST14LivingBarricadeComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnBarricadeShutdown(Entity<ST14LivingBarricadeComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnBarricade(Entity<ST14LivingBarricadeComponent> ent, ref ST14BugBarricadeActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        // any bug of the wall can let it fall apart
        if (TryComp<ST14InBarricadeComponent>(ent, out var inWall))
        {
            BreakBarricade(inWall.Members.ToList());
            return;
        }

        if (TryComp<ST14HiveGrabComponent>(ent, out var grab) && grab.Victim != null)
        {
            _popup.PopupEntity(Loc.GetString("st14-bug-barricade-busy"), ent, ent);
            return;
        }

        var members = new List<EntityUid> { ent };

        _barricadeCandidates.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.JoinRange, _barricadeCandidates);

        var origin = _transform.GetWorldPosition(ent);
        foreach (var other in _barricadeCandidates.OrderBy(o => (_transform.GetWorldPosition(o) - origin).LengthSquared()))
        {
            if (members.Count >= ent.Comp.MaxMembers)
                break;

            if (other.Owner == ent.Owner ||
                _mobState.IsDead(other) ||
                HasComp<ST14InBarricadeComponent>(other) ||
                TryComp<ST14HiveGrabComponent>(other, out var otherGrab) && otherGrab.Victim != null)
                continue;

            members.Add(other);
        }

        var end = _timing.CurTime + ent.Comp.Duration;
        foreach (var member in members)
        {
            var wall = EnsureComp<ST14InBarricadeComponent>(member);
            wall.Members = new List<EntityUid>(members);
            wall.End = end;

            // anything the bug was dragging is let go, its legs are locked
            if (TryComp<PullerComponent>(member, out var puller) &&
                TryComp<PullableComponent>(puller.Pulling, out var pulled))
                _pulling.TryStopPull(puller.Pulling.Value, pulled, member);

            var proto = CompOrNull<ST14LivingBarricadeComponent>(member)?.Shield ?? ent.Comp.Shield;
            var shield = Spawn(proto, Transform(member).Coordinates);
            EnsureComp<ST14BarricadeShieldComponent>(shield).Bug = member;
            wall.Shield = shield;

            Dirty(member, wall);
            _popup.PopupEntity(Loc.GetString("st14-bug-barricade-joined", ("count", members.Count)), member, member, PopupType.Medium);
        }

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);

        _popup.PopupEntity(Loc.GetString("st14-bug-barricade-formed"), ent, PopupType.LargeCaution);
    }

    /// <summary>
    /// The wall falls apart: the bugs walk again and wait out the cooldown
    /// </summary>
    private void BreakBarricade(List<EntityUid> members)
    {
        var sounded = false;
        foreach (var member in members)
        {
            if (!TryComp<ST14InBarricadeComponent>(member, out var wall))
                continue;

            LeaveBarricade((member, wall));

            if (!TryComp<ST14LivingBarricadeComponent>(member, out var barricade))
                continue;

            _actions.SetCooldown(barricade.ActionEntity, barricade.Cooldown);

            if (!sounded && barricade.BreakSound != null)
            {
                _audio.PlayPvs(barricade.BreakSound, member);
                sounded = true;
            }
        }
    }

    /// <summary>
    /// Takes one bug out of its wall, the rest of the wall stands on
    /// </summary>
    private void LeaveBarricade(Entity<ST14InBarricadeComponent> ent)
    {
        if (ent.Comp.Shield is { } shield)
            QueueDel(shield);

        // the rest of the wall stands without this bug
        foreach (var member in ent.Comp.Members)
        {
            if (member == ent.Owner || !TryComp<ST14InBarricadeComponent>(member, out var other))
                continue;

            other.Members.Remove(ent.Owner);
            Dirty(member, other);
        }

        RemComp(ent, ent.Comp);
    }

    private void OnBarricadeMobState(Entity<ST14InBarricadeComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            LeaveBarricade(ent);
    }

    private void OnBarricadeBeforeDamage(Entity<ST14InBarricadeComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (_sharingDamage || args.Cancelled || !args.Damage.AnyPositive())
            return;

        var members = ent.Comp.Members.Where(m => !TerminatingOrDeleted(m) && !_mobState.IsDead(m)).ToList();
        if (members.Count < 2)
            return;

        args.Cancelled = true;

        var share = args.Damage / (float) members.Count;
        _sharingDamage = true;
        try
        {
            foreach (var member in members)
            {
                _damage.TryChangeDamage(member, share, origin: args.Origin);
            }
        }
        finally
        {
            _sharingDamage = false;
        }
    }

    private void OnShieldBeforeDamage(Entity<ST14BarricadeShieldComponent> ent, ref BeforeDamageChangedEvent args)
    {
        // the shield itself is never hurt, the bullet lands on the wall
        args.Cancelled = true;

        if (!TerminatingOrDeleted(ent.Comp.Bug))
            _damage.TryChangeDamage(ent.Comp.Bug, args.Damage, origin: args.Origin);
    }

    #endregion
}
