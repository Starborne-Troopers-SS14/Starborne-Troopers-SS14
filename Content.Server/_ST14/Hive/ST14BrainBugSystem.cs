using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Traits.Assorted;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._ST14.Hive;

/// <summary>
/// The brain bug pierces the skull of a live trooper and sucks out the brain. The trooper is gone for good
/// and the hive gets points, more for command
/// </summary>
public sealed partial class ST14BrainBugSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ST14HiveSystem _hive = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14BrainBugComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14BrainBugComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ST14BrainBugComponent, ST14BrainSuckActionEvent>(OnSuckAction);
        SubscribeLocalEvent<ST14BrainBugComponent, ST14BrainSuckDoAfterEvent>(OnSuckDoAfter);
    }

    private void OnMapInit(Entity<ST14BrainBugComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<ST14BrainBugComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnSuckAction(Entity<ST14BrainBugComponent> ent, ref ST14BrainSuckActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryStartSuck(ent, args.Target, true);
    }

    /// <summary>
    /// Starts sucking the brain of a trooper next to the brain bug
    /// </summary>
    public bool TryStartSuck(Entity<ST14BrainBugComponent> ent, EntityUid target, bool popup)
    {
        if (ent.Comp.Sucking || _mobState.IsDead(ent) || !CanSuck(ent, target, popup))
            return false;

        var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.SuckTime, new ST14BrainSuckDoAfterEvent(), ent, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 2f,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        ent.Comp.Sucking = true;
        _appearance.SetData(ent, ST14BrainBugVisuals.Sucking, true);

        if (ent.Comp.SuckStartSound != null)
            _audio.PlayPvs(ent.Comp.SuckStartSound, ent);

        _popup.PopupEntity(Loc.GetString("st14-brain-suck-start", ("target", target)), ent, PopupType.LargeCaution);
        return true;
    }

    private bool CanSuck(Entity<ST14BrainBugComponent> ent, EntityUid target, bool popup)
    {
        if (!HasComp<HumanoidProfileComponent>(target) || HasComp<ST14HiveMemberComponent>(target))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("st14-brain-suck-not-trooper"), ent, ent);
            return false;
        }

        if (_mobState.IsDead(target) || HasComp<UnrevivableComponent>(target))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("st14-brain-suck-dead"), ent, ent);
            return false;
        }

        return true;
    }

    private void OnSuckDoAfter(Entity<ST14BrainBugComponent> ent, ref ST14BrainSuckDoAfterEvent args)
    {
        ent.Comp.Sucking = false;
        _appearance.SetData(ent, ST14BrainBugVisuals.Sucking, false);

        if (args.Handled || args.Cancelled || args.Target is not { } target || !CanSuck(ent, target, false))
            return;

        args.Handled = true;

        var command = IsCommand(ent.Comp, target);
        var points = command
            ? _random.Next(ent.Comp.CommandMin, ent.Comp.CommandMax + 1)
            : _random.Next(ent.Comp.RegularMin, ent.Comp.RegularMax + 1);
        points = (int) MathF.Round(points * _hive.GetOnlineMultiplier(_player.PlayerCount));

        // Whoever dragged the trooper here gets their share
        if (TryComp<PullableComponent>(target, out var pullable) &&
            pullable.Puller is { } puller &&
            TryComp<ST14HiveMemberComponent>(puller, out var pullerMember))
        {
            _hive.AddEvolutionPoints((puller, pullerMember), pullerMember.DeliveryPoints);
        }

        // No brain, no revival
        EnsureComp<UnrevivableComponent>(target);
        _mobState.ChangeMobState(target, MobState.Dead, origin: ent);

        if (ent.Comp.SuckSound != null)
            _audio.PlayPvs(ent.Comp.SuckSound, ent);

        _popup.PopupEntity(Loc.GetString("st14-brain-suck-done", ("target", target)), ent, PopupType.LargeCaution);
        _hive.AddHivePoints(points);

        var query = EntityQueryEnumerator<ST14HiveMemberComponent>();
        var message = Loc.GetString(command ? "st14-brain-suck-hive-command" : "st14-brain-suck-hive", ("points", points));
        while (query.MoveNext(out var uid, out _))
        {
            _popup.PopupEntity(message, uid, uid, PopupType.Medium);
        }
    }

    private bool IsCommand(ST14BrainBugComponent brain, EntityUid target)
    {
        if (!_mind.TryGetMind(target, out var mindId, out _) || !_jobs.MindTryGetJobId(mindId, out var job) || job == null)
            return false;

        if (brain.CommandJobs.Contains(job.Value))
            return true;

        foreach (var department in brain.CommandDepartments)
        {
            if (_prototype.Resolve(department, out var proto) && proto.Roles.Contains(job.Value))
                return true;
        }

        return false;
    }
}
