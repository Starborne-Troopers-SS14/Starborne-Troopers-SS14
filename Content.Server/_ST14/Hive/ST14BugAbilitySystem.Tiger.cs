using System.Numerics;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Hive;

/// <summary>
/// The tiger: execution of the wounded, the scent of their blood and the last rush
/// </summary>
public sealed partial class ST14BugAbilitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;

    private static readonly ProtoId<DamageTypePrototype> ExecutionDamage = "Slash";

    // Blood trails: how often they are sent, how long a drop stays, how far apart drops are and how long a trail gets
    private static readonly TimeSpan ScentInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan ScentLifetime = TimeSpan.FromSeconds(20);
    private const float ScentStep = 0.45f;
    private const float ScentLength = 10f;

    /// <summary>
    /// Troopers with less health left than this part of it leave a trail
    /// </summary>
    private const float ScentHealth = 0.5f;

    private readonly Dictionary<EntityUid, ScentTrail> _trails = new();
    private readonly List<EntityUid> _trailsToRemove = new();
    private readonly List<Entity<ST14LastRushComponent>> _rushOver = new();
    private TimeSpan _nextScent;

    private void InitializeTiger()
    {
        SubscribeLocalEvent<ST14BugExecuteComponent, MapInitEvent>(OnExecuteMapInit);
        SubscribeLocalEvent<ST14BugExecuteComponent, ComponentShutdown>(OnExecuteShutdown);
        SubscribeLocalEvent<ST14BugExecuteComponent, ST14BugExecuteActionEvent>(OnExecute);
        SubscribeLocalEvent<ST14BugExecutingComponent, ThrowDoHitEvent>(OnExecuteHit);
        SubscribeLocalEvent<ST14BugExecutingComponent, LandEvent>(OnExecuteLand);
        SubscribeLocalEvent<ST14BugExecutingComponent, StopThrowEvent>(OnExecuteStop);
    }

    private void UpdateTiger(float frameTime)
    {
        var now = _timing.CurTime;

        _rushOver.Clear();
        var rush = EntityQueryEnumerator<ST14LastRushComponent>();
        while (rush.MoveNext(out var uid, out var comp))
        {
            if (comp.Active && now >= comp.End)
                _rushOver.Add((uid, comp));
        }

        foreach (var ent in _rushOver)
        {
            EndLastRush(ent);
        }

        if (now < _nextScent)
            return;

        _nextScent = now + ScentInterval;
        UpdateScent(now);
    }

    #region Execution

    private void OnExecuteMapInit(Entity<ST14BugExecuteComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnExecuteShutdown(Entity<ST14BugExecuteComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnExecute(Entity<ST14BugExecuteComponent> ent, ref ST14BugExecuteActionEvent args)
    {
        if (args.Handled)
            return;

        var target = args.Target;
        if (!HasComp<MobStateComponent>(target) || _hive.IsHive(target) || _mobState.IsDead(target))
        {
            _popup.PopupEntity(Loc.GetString("st14-bug-execute-invalid"), ent, ent);
            return;
        }

        args.Handled = true;
        EnsureComp<ST14BugExecutingComponent>(ent).Target = target;

        if (ent.Comp.LeapSound != null)
            _audio.PlayPvs(ent.Comp.LeapSound, ent);

        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.GetMapCoordinates(target);
        var direction = to.Position - from.Position;

        // right next to the victim there is nothing to leap over
        if (from.MapId != to.MapId ||
            direction.Length() <= ent.Comp.StrikeRange ||
            !_throwing.TryThrow(ent, direction, ent.Comp.Speed, ent, recoil: false, doSpin: false))
        {
            Strike(ent);
        }
    }

    private void OnExecuteHit(Entity<ST14BugExecutingComponent> ent, ref ThrowDoHitEvent args)
    {
        if (args.Target == ent.Comp.Target)
            Strike(ent);
    }

    private void OnExecuteLand(Entity<ST14BugExecutingComponent> ent, ref LandEvent args)
    {
        Strike(ent);
    }

    private void OnExecuteStop(Entity<ST14BugExecutingComponent> ent, ref StopThrowEvent args)
    {
        Strike(ent);
    }

    /// <summary>
    /// The end of the leap: the victim is executed if it is wounded badly enough and still in reach
    /// </summary>
    private void Strike(EntityUid bug)
    {
        if (!TryComp<ST14BugExecutingComponent>(bug, out var executing) ||
            !TryComp<ST14BugExecuteComponent>(bug, out var execute))
            return;

        var target = executing.Target;
        RemComp(bug, executing);

        if (TerminatingOrDeleted(target) || _mobState.IsDead(target) || _mobState.IsDead(bug))
            return;

        var from = _transform.GetMapCoordinates(bug);
        var to = _transform.GetMapCoordinates(target);
        if (from.MapId != to.MapId || (to.Position - from.Position).Length() > execute.StrikeRange)
        {
            _popup.PopupEntity(Loc.GetString("st14-bug-execute-miss"), bug, bug);
            return;
        }

        if (!CanExecute(target, execute.HealthThreshold))
        {
            _damage.TryChangeDamage(target, execute.Damage * _shared.RageMultiplier(bug), origin: bug);
            _popup.PopupEntity(Loc.GetString("st14-bug-execute-healthy", ("target", target)), bug, bug);
            return;
        }

        if (_thresholds.TryGetThresholdForState(target, MobState.Dead, out var dead))
        {
            var finish = dead.Value - _damage.GetTotalDamage(target) + 1;
            if (finish > 0)
            {
                var blow = new DamageSpecifier(_prototype.Index(ExecutionDamage), finish);
                _damage.TryChangeDamage(target, blow, ignoreResistances: true, origin: bug);
            }
        }

        if (!_mobState.IsDead(target))
            _mobState.ChangeMobState(target, MobState.Dead, origin: bug);

        _damage.HealEvenly(bug, -execute.Heal, origin: bug);
        _actions.SetCooldown(execute.ActionEntity, execute.ResetCooldown);

        if (execute.KillSound != null)
            _audio.PlayPvs(execute.KillSound, bug);

        _popup.PopupEntity(Loc.GetString("st14-bug-execute-kill", ("target", target)), target, PopupType.LargeCaution);
    }

    /// <summary>
    /// A trooper in crit, or with less health left than the given part of it, can be executed
    /// </summary>
    private bool CanExecute(EntityUid target, float healthThreshold)
    {
        if (_mobState.IsCritical(target))
            return true;

        return HealthLeft(target) is { } health && health < healthThreshold;
    }

    /// <summary>
    /// Part of the health left before the mob goes down, null when it cannot go down at all
    /// </summary>
    private float? HealthLeft(EntityUid uid)
    {
        if (!_thresholds.TryGetIncapThreshold(uid, out var incap) || incap.Value <= 0)
            return null;

        return 1f - (_damage.GetTotalDamage(uid) / incap.Value).Float();
    }

    #endregion

    #region Blood scent

    private sealed class ScentTrail
    {
        public MapId Map;
        public readonly List<(Vector2 Position, TimeSpan Time)> Points = new();
    }

    private void UpdateScent(TimeSpan now)
    {
        var query = EntityQueryEnumerator<HumanoidProfileComponent, MobStateComponent, DamageableComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var state, out _, out var xform))
        {
            if (state.CurrentState == MobState.Dead ||
                _hive.IsHive(uid) ||
                HealthLeft(uid) is not { } health ||
                health >= ScentHealth)
            {
                // healed or dead: nothing to smell any more
                _trails.Remove(uid);
                continue;
            }

            var position = _transform.GetMapCoordinates(uid, xform);
            if (!_trails.TryGetValue(uid, out var trail) || trail.Map != position.MapId)
            {
                trail = new ScentTrail { Map = position.MapId };
                _trails[uid] = trail;
            }

            if (trail.Points.Count == 0 || (trail.Points[^1].Position - position.Position).Length() >= ScentStep)
                trail.Points.Add((position.Position, now));
        }

        _trailsToRemove.Clear();
        foreach (var (uid, trail) in _trails)
        {
            if (TerminatingOrDeleted(uid))
            {
                _trailsToRemove.Add(uid);
                continue;
            }

            trail.Points.RemoveAll(p => now - p.Time > ScentLifetime);

            // a trail never grows longer than its length, the oldest drops dry up first
            var length = 0f;
            for (var i = trail.Points.Count - 1; i > 0; i--)
            {
                length += (trail.Points[i].Position - trail.Points[i - 1].Position).Length();
                if (length <= ScentLength)
                    continue;

                trail.Points.RemoveRange(0, i);
                break;
            }
        }

        foreach (var uid in _trailsToRemove)
        {
            _trails.Remove(uid);
        }

        SendScent(now);
    }

    private void SendScent(TimeSpan now)
    {
        var filter = Filter.Empty();
        var query = EntityQueryEnumerator<ST14BloodScentComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out _, out var actor))
        {
            if (!_mobState.IsDead(uid))
                filter.AddPlayer(actor.PlayerSession);
        }

        if (filter.Count == 0)
            return;

        var trails = new List<ST14BloodTrail>(_trails.Count);
        foreach (var (uid, trail) in _trails)
        {
            var message = new ST14BloodTrail
            {
                Trooper = GetNetEntity(uid),
                Map = trail.Map,
            };

            foreach (var (position, time) in trail.Points)
            {
                message.Points.Add(position);
                message.Ages.Add((float) (now - time).TotalSeconds);
            }

            trails.Add(message);
        }

        RaiseNetworkEvent(new ST14BloodScentEvent(trails), filter);
    }

    #endregion

    #region Last rush

    private void EndLastRush(Entity<ST14LastRushComponent> ent)
    {
        ent.Comp.Active = false;
        Dirty(ent);

        if (_mobState.IsDead(ent) || !_thresholds.TryGetThresholdForState(ent, MobState.Dead, out var dead))
            return;

        // the rage burns out and so does the bug
        var finish = dead.Value - _damage.GetTotalDamage(ent.Owner) + 1;
        if (finish > 0)
            _damage.TryChangeDamage(ent.Owner, new DamageSpecifier(_prototype.Index(ExecutionDamage), finish), ignoreResistances: true);

        if (!_mobState.IsDead(ent))
            _mobState.ChangeMobState(ent, MobState.Dead);
    }

    #endregion
}
