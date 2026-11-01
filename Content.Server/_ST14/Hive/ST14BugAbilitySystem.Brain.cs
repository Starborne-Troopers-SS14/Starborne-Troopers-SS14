using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Powers of the brain bug: a stupor that freezes one trooper in the worst visions, and a scream that fills
/// every head around with visions and ringing
/// </summary>
public sealed partial class ST14BugAbilitySystem
{
    private readonly List<EntityUid> _mindChecks = new();

    private void InitializeBrain()
    {
        SubscribeLocalEvent<ST14BrainStuporComponent, MapInitEvent>(OnStuporMapInit);
        SubscribeLocalEvent<ST14BrainStuporComponent, ComponentShutdown>(OnStuporShutdown);
        SubscribeLocalEvent<ST14BrainStuporComponent, ST14BrainStuporActionEvent>(OnStupor);

        SubscribeLocalEvent<ST14BrainScreamComponent, MapInitEvent>(OnScreamMapInit);
        SubscribeLocalEvent<ST14BrainScreamComponent, ComponentShutdown>(OnScreamShutdown);
        SubscribeLocalEvent<ST14BrainScreamComponent, ST14BrainScreamActionEvent>(OnScream);
    }

    private void UpdateBrain(TimeSpan now)
    {
        _mindChecks.Clear();
        var visions = EntityQueryEnumerator<ST14HallucinatingComponent>();
        while (visions.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.End || comp.Paralyzed && now >= comp.ParalyzedEnd)
                _mindChecks.Add(uid);
        }

        foreach (var uid in _mindChecks)
        {
            if (!TryComp<ST14HallucinatingComponent>(uid, out var comp))
                continue;

            if (comp.Paralyzed && now >= comp.ParalyzedEnd)
            {
                _shared.SetParalyzed((uid, comp), false);
                _popup.PopupEntity(Loc.GetString("st14-brain-stupor-free"), uid, uid, PopupType.Medium);
            }

            if (now >= comp.End)
                RemComp(uid, comp);
        }

        _mindChecks.Clear();
        var deaf = EntityQueryEnumerator<ST14DeafenedComponent>();
        while (deaf.MoveNext(out var uid, out var comp))
        {
            if (now >= comp.End)
                _mindChecks.Add(uid);
        }

        foreach (var uid in _mindChecks)
        {
            RemComp<ST14DeafenedComponent>(uid);
        }
    }

    /// <summary>
    /// Fills the trooper's screen with visions. A stronger or longer dose replaces a weaker one, never the other way
    /// </summary>
    private ST14HallucinatingComponent Hallucinate(EntityUid target, TimeSpan duration, float intensity)
    {
        var end = _timing.CurTime + duration;
        var visions = EnsureComp<ST14HallucinatingComponent>(target);
        if (end > visions.End)
            visions.End = end;

        visions.Intensity = MathF.Max(visions.Intensity, intensity);
        Dirty(target, visions);
        return visions;
    }

    private void Deafen(EntityUid target, TimeSpan duration)
    {
        var end = _timing.CurTime + duration;
        var deaf = EnsureComp<ST14DeafenedComponent>(target);
        if (end > deaf.End)
            deaf.End = end;
    }

    #region Stupor

    private void OnStuporMapInit(Entity<ST14BrainStuporComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnStuporShutdown(Entity<ST14BrainStuporComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnStupor(Entity<ST14BrainStuporComponent> ent, ref ST14BrainStuporActionEvent args)
    {
        if (args.Handled)
            return;

        var self = ent.Owner;
        if (PickTarget(args.Target, uid => uid != self && !_hive.IsHive(uid) && !_mobState.IsDead(uid)) is not { } target)
        {
            _popup.PopupEntity(Loc.GetString("st14-brain-stupor-invalid"), ent, ent);
            return;
        }

        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.GetMapCoordinates(target);
        if (from.MapId != to.MapId || (to.Position - from.Position).Length() > ent.Comp.Range)
        {
            _popup.PopupEntity(Loc.GetString("st14-brain-stupor-far"), ent, ent);
            return;
        }

        args.Handled = true;

        var visions = Hallucinate(target, ent.Comp.Duration + TimeSpan.FromSeconds(2), 1f);
        visions.ParalyzedEnd = _timing.CurTime + ent.Comp.Duration;
        _shared.SetParalyzed((target, visions), true);
        Deafen(target, ent.Comp.Duration);

        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, ent);

        if (ent.Comp.VictimSound != null)
            _audio.PlayEntity(ent.Comp.VictimSound, Filter.Entities(target), target, false);

        _popup.PopupEntity(Loc.GetString("st14-brain-stupor-victim"), target, target, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("st14-brain-stupor-done", ("target", target)), ent, ent, PopupType.Medium);
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

        _victims.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _victims);
        foreach (var victim in _victims)
        {
            if (_hive.IsHive(victim) || _mobState.IsDead(victim))
                continue;

            Hallucinate(victim, ent.Comp.Duration, ent.Comp.Intensity);
            Deafen(victim, ent.Comp.Duration);

            if (ent.Comp.RingingSound != null)
                _audio.PlayEntity(ent.Comp.RingingSound, Filter.Entities(victim.Owner), victim, false);

            _popup.PopupEntity(Loc.GetString("st14-brain-scream-victim"), victim, victim, PopupType.LargeCaution);
        }
    }

    #endregion
}
