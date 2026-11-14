using System.Numerics;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// Удары луча: кто вошёл в луч и подходит под белый список, получает разряд, оглушение и отлетает от луча
/// </summary>
public sealed partial class ST14EnergyFenceSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    /// <summary>
    /// Общая для всех сегментов задержка: жук на стыке двух сегментов не получает двойной удар
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextHit = new();

    private readonly List<EntityUid> _hitTargets = new();
    private readonly List<EntityUid> _staleHits = new();

    private void InitializeBeam()
    {
        SubscribeLocalEvent<ST14EnergyBeamComponent, StartCollideEvent>(OnBeamStartCollide);
        SubscribeLocalEvent<ST14EnergyBeamComponent, EndCollideEvent>(OnBeamEndCollide);
    }

    private void OnBeamStartCollide(Entity<ST14EnergyBeamComponent> ent, ref StartCollideEvent args)
    {
        if (!CanHit(ent.Comp, args.OtherEntity))
            return;

        ent.Comp.Contacts.Add(args.OtherEntity);
    }

    private void OnBeamEndCollide(Entity<ST14EnergyBeamComponent> ent, ref EndCollideEvent args)
    {
        ent.Comp.Contacts.Remove(args.OtherEntity);
    }

    private bool CanHit(ST14EnergyBeamComponent beam, EntityUid target)
    {
        return _whitelist.IsWhitelistPassOrNull(beam.Whitelist, target)
               && !_whitelist.IsWhitelistPass(beam.Blacklist, target);
    }

    private void UpdateBeams(TimeSpan now)
    {
        var query = EntityQueryEnumerator<ST14EnergyBeamComponent>();
        while (query.MoveNext(out var uid, out var beam))
        {
            if (beam.Contacts.Count == 0)
                continue;

            _hitTargets.Clear();
            foreach (var target in beam.Contacts)
            {
                if (TerminatingOrDeleted(target))
                    continue;

                if (_nextHit.TryGetValue(target, out var next) && now < next)
                    continue;

                _hitTargets.Add(target);
            }

            beam.Contacts.RemoveWhere(e => TerminatingOrDeleted(e));

            foreach (var target in _hitTargets)
            {
                if (!Zap((uid, beam), target))
                    break;

                _nextHit[target] = now + beam.HitDelay;
            }
        }
    }

    /// <summary>
    /// Один разряд по цели. Возвращает false, если у луча кончилось питание
    /// </summary>
    private bool Zap(Entity<ST14EnergyBeamComponent> beam, EntityUid target)
    {
        if (_mobState.IsDead(target))
            return true;

        if (!TryDrain(beam.Comp))
        {
            _dirty = true;
            return false;
        }

        _damage.TryChangeDamage(target, beam.Comp.Damage, origin: beam.Owner);
        _stun.TryUpdateParalyzeDuration(target, beam.Comp.StunTime);

        var coords = Transform(target).Coordinates;
        _audio.PlayPvs(beam.Comp.HitSound, coords);
        if (beam.Comp.HitEffect is { } effect)
            Spawn(effect, coords);

        if (beam.Comp.ThrowDistance > 0f)
            _throwing.TryThrow(target, GetThrowDirection(beam, target) * beam.Comp.ThrowDistance, baseThrowSpeed: 6f);

        return true;
    }

    /// <summary>
    /// Отбрасывает поперёк луча, в ту сторону, откуда цель пришла
    /// </summary>
    private Vector2 GetThrowDirection(Entity<ST14EnergyBeamComponent> beam, EntityUid target)
    {
        var offset = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(beam);
        var side = beam.Comp.Vertical ? offset.X : offset.Y;

        if (MathF.Abs(side) < 0.05f && TryComp<PhysicsComponent>(target, out var physics))
            side = -(beam.Comp.Vertical ? physics.LinearVelocity.X : physics.LinearVelocity.Y);

        var sign = MathF.Abs(side) < 0.001f ? (_random.Prob(0.5f) ? 1f : -1f) : MathF.Sign(side);
        return beam.Comp.Vertical ? new Vector2(sign, 0f) : new Vector2(0f, sign);
    }

    /// <summary>
    /// Списывает заряд удара с энергоблока щитка, который питает луч
    /// </summary>
    private bool TryDrain(ST14EnergyBeamComponent beam)
    {
        if (beam.Source is not { } source
            || !TryComp<ST14EnergyPylonComponent>(source, out var pylon)
            || pylon.Box is not { } boxUid
            || !TryComp<ST14EnergyBoxComponent>(boxUid, out var box)
            || GetBlock((boxUid, box)) is not { } block)
        {
            return false;
        }

        var charge = _battery.GetCharge(block).Charge;
        if (charge <= 0f)
            return false;

        _battery.UseCharge(block, MathF.Min(charge, beam.ChargePerHit));
        if (_battery.GetCharge(block).Charge <= 0f)
            _dirty = true;

        return true;
    }

    private void CleanupHits()
    {
        var now = _timing.CurTime;
        _staleHits.Clear();
        foreach (var (target, next) in _nextHit)
        {
            if (next < now || TerminatingOrDeleted(target))
                _staleHits.Add(target);
        }

        foreach (var target in _staleHits)
            _nextHit.Remove(target);
    }
}
