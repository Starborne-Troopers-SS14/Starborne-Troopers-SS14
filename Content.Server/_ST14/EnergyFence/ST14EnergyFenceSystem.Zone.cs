using System.Numerics;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// The shock zone of a link: a capsule of <see cref="ST14EnergyPylonComponent.ZoneRadius"/> around the segment between
/// two powered pylons. The deeper a bug is (<c>1 - dist / radius</c>), the harder, the more often it is hit,
/// the slower it walks; past <see cref="ST14EnergyPylonComponent.StunDepth"/> it is stuck.
/// Bugs are <c>Insulated</c> against wires and tesla, so the shock is dealt here directly instead of through ElectrocutionSystem
/// </summary>
public sealed partial class ST14EnergyFenceSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ST14SharedEnergyFenceSystem _shared = default!;

    private static readonly ProtoId<DamageTypePrototype> ShockDamage = "Shock";

    /// <summary>
    /// Depths are sent to clients rounded to this step, so a walking bug does not dirty the pylon every tick
    /// </summary>
    private const float DepthStep = 0.05f;

    /// <summary>
    /// When each target may be hit again. Shared by all links, so a bug between two links is not hit twice as often
    /// </summary>
    private readonly Dictionary<EntityUid, TimeSpan> _nextHit = new();

    /// <summary>
    /// Deepest depth of every target this tick and the pylon whose link holds it there
    /// </summary>
    private readonly Dictionary<EntityUid, (float Depth, EntityUid Pylon)> _targets = new();

    private readonly HashSet<Entity<MobStateComponent>> _found = new();
    private readonly List<EntityUid> _stale = new();
    private readonly Dictionary<EntityUid, List<ST14FenceLinkState>> _states = new();

    private void UpdateZones(TimeSpan now)
    {
        _targets.Clear();

        foreach (var link in _links)
        {
            link.Wrapped.Clear();

            if (!TryComp<ST14EnergyPylonComponent>(link.A, out var pylonA)
                || !TryComp<ST14EnergyPylonComponent>(link.B, out var pylonB)
                || !pylonA.Powered
                || !pylonB.Powered)
            {
                link.Active = false;
                continue;
            }

            FindTargets(link, pylonA);

            if (link.Wrapped.Count > 0)
            {
                link.Active = true;
                link.ActiveUntil = now + pylonA.Linger;
            }
            else if (now >= link.ActiveUntil)
            {
                link.Active = false;
            }

            foreach (var (target, depth) in link.Wrapped)
            {
                if (!_targets.TryGetValue(target, out var best) || best.Depth < depth)
                    _targets[target] = (depth, link.A);
            }
        }

        foreach (var (target, (depth, pylonUid)) in _targets)
        {
            var pylon = Comp<ST14EnergyPylonComponent>(pylonUid);
            var shocked = EnsureComp<ST14FenceShockedComponent>(target);
            _shared.SetSpeed((target, shocked), depth, 1f - pylon.DepthSlowdown * depth);

            // A bug that just walked in is hit at once
            if (_nextHit.TryGetValue(target, out var next) && now < next)
                continue;

            Hit(target, pylonUid, pylon, depth);
            _nextHit[target] = now + TimeSpan.FromSeconds(MathF.Max(0.05f, pylon.BaseInterval - pylon.DepthIntervalFactor * depth));
        }

        // Out of every zone: back to full speed. The hit timer is kept until it runs out, so stepping out and back in does not reset it
        _stale.Clear();
        var shockedQuery = EntityQueryEnumerator<ST14FenceShockedComponent>();
        while (shockedQuery.MoveNext(out var uid, out _))
        {
            if (!_targets.ContainsKey(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            RemComp<ST14FenceShockedComponent>(uid);
        }

        _stale.Clear();
        foreach (var (uid, next) in _nextHit)
        {
            if (next < now && !_targets.ContainsKey(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _nextHit.Remove(uid);
        }

        SyncLinks();
    }

    /// <summary>
    /// Every living whitelisted mob inside the capsule of the link, with its depth
    /// </summary>
    private void FindTargets(FenceLink link, ST14EnergyPylonComponent pylon)
    {
        var xformA = Transform(link.A);
        var xformB = Transform(link.B);
        if (xformA.MapID != xformB.MapID)
            return;

        var a = _transform.GetWorldPosition(xformA);
        var b = _transform.GetWorldPosition(xformB);
        var center = (a + b) / 2f;
        var range = (b - a).Length() / 2f + pylon.ZoneRadius;

        _found.Clear();
        _lookup.GetEntitiesInRange(xformA.MapID, center, range, _found);

        foreach (var mob in _found)
        {
            if (_mobState.IsDead(mob, mob.Comp)
                || !_whitelist.IsWhitelistPassOrNull(pylon.Whitelist, mob)
                || _whitelist.IsWhitelistPass(pylon.Blacklist, mob))
            {
                continue;
            }

            var dist = DistanceToSegment(_transform.GetWorldPosition(mob), a, b);
            if (dist >= pylon.ZoneRadius)
                continue;

            var depth = 1f - Math.Clamp(dist / pylon.ZoneRadius, 0f, 1f);
            link.Wrapped[mob] = depth;
        }
    }

    public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var lengthSquared = ab.LengthSquared();
        if (lengthSquared < 0.0001f)
            return (point - a).Length();

        var t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);
        return (point - (a + ab * t)).Length();
    }

    private void Hit(EntityUid target, EntityUid pylonUid, ST14EnergyPylonComponent pylon, float depth)
    {
        var amount = pylon.BaseDamage * (pylon.MinDamageFactor + pylon.DepthDamageFactor * depth * depth);
        var damage = new DamageSpecifier();
        damage.DamageDict[ShockDamage] = amount;
        _damage.TryChangeDamage(target, damage, origin: pylonUid);

        if (depth > pylon.StunDepth)
            _stun.TryUpdateParalyzeDuration(target, pylon.StunTime);

        var interval = pylon.BaseInterval - pylon.DepthIntervalFactor * depth;
        _jitter.DoJitter(target,
            TimeSpan.FromSeconds(interval + 0.3f),
            true,
            amplitude: 8f + 16f * depth,
            frequency: 4f + 6f * depth,
            forceValueChange: true);

        var coords = Transform(target).Coordinates;
        _audio.PlayPvs(pylon.HitSound, coords);
        if (pylon.HitEffect is { } effect)
            Spawn(effect, coords);
    }

    /// <summary>
    /// Writes the links into the networked state of their first pylons, dirtying only what changed
    /// </summary>
    private void SyncLinks()
    {
        foreach (var list in _states.Values)
        {
            list.Clear();
        }

        foreach (var link in _links)
        {
            if (!_states.TryGetValue(link.A, out var list))
            {
                list = new List<ST14FenceLinkState>();
                _states[link.A] = list;
            }

            var wrapped = new NetEntity[link.Wrapped.Count];
            var depths = new float[link.Wrapped.Count];
            var i = 0;
            foreach (var (target, depth) in link.Wrapped)
            {
                wrapped[i] = GetNetEntity(target);
                depths[i] = MathF.Round(depth / DepthStep) * DepthStep;
                i++;
            }

            list.Add(new ST14FenceLinkState(GetNetEntity(link.B), link.Active, wrapped, depths));
        }

        var query = EntityQueryEnumerator<ST14EnergyPylonComponent>();
        while (query.MoveNext(out var uid, out var pylon))
        {
            if (!_states.TryGetValue(uid, out var list) || list.Count == 0)
            {
                if (pylon.Links.Count == 0)
                    continue;

                pylon.Links = new List<ST14FenceLinkState>();
                Dirty(uid, pylon);
                continue;
            }

            if (SameLinks(pylon.Links, list))
                continue;

            pylon.Links = new List<ST14FenceLinkState>(list);
            Dirty(uid, pylon);
        }

        // Pylons that are gone
        _stale.Clear();
        foreach (var uid in _states.Keys)
        {
            if (!Exists(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            _states.Remove(uid);
        }
    }

    private static bool SameLinks(List<ST14FenceLinkState> old, List<ST14FenceLinkState> now)
    {
        if (old.Count != now.Count)
            return false;

        for (var i = 0; i < old.Count; i++)
        {
            var a = old[i];
            var b = now[i];
            if (a.Other != b.Other || a.Active != b.Active || a.Wrapped.Length != b.Wrapped.Length)
                return false;

            for (var j = 0; j < a.Wrapped.Length; j++)
            {
                if (a.Wrapped[j] != b.Wrapped[j] || !MathHelper.CloseTo(a.Depths[j], b.Depths[j]))
                    return false;
            }
        }

        return true;
    }
}
