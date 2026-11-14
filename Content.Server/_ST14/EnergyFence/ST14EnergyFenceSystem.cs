using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Audio;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// Fence pylons: pairing, power and looks. The shock zone of the links is in the .Zone part.
/// Pylons pair up greedily (all pairs in range, nearest first, a pair is taken while both ends have free links);
/// the pairing is rebuilt whenever a pylon is anchored, unanchored or removed
/// </summary>
public sealed partial class ST14EnergyFenceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private PowerReceiverSystem _receiver = default!;

    /// <summary>
    /// A pair of linked pylons. <see cref="A"/> is the one that carries the link in its networked state
    /// </summary>
    private sealed class FenceLink
    {
        public EntityUid A;
        public EntityUid B;
        public bool Active;
        public TimeSpan ActiveUntil;
        public readonly Dictionary<EntityUid, float> Wrapped = new();
    }

    private readonly List<FenceLink> _links = new();
    private readonly List<(EntityUid Uid, ST14EnergyPylonComponent Comp, MapId Map, Vector2 Pos)> _pylons = new();
    private readonly List<(float Dist, int I, int J)> _pairs = new();
    private readonly Dictionary<EntityUid, int> _linkCounts = new();
    private readonly Dictionary<EntityUid, int> _activeCounts = new();
    private readonly Dictionary<EntityUid, int> _wrappedCounts = new();

    private bool _dirty = true;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14EnergyPylonComponent, ComponentStartup>(OnPylonChanged);
        SubscribeLocalEvent<ST14EnergyPylonComponent, ComponentShutdown>(OnPylonChanged);
        SubscribeLocalEvent<ST14EnergyPylonComponent, AnchorStateChangedEvent>(OnPylonAnchor);
        SubscribeLocalEvent<ST14EnergyPylonComponent, EntityUnpausedEvent>(OnPylonChanged);
        SubscribeLocalEvent<ST14EnergyPylonComponent, ExaminedEvent>(OnPylonExamined);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

    }

    private void OnPylonChanged<T>(Entity<ST14EnergyPylonComponent> ent, ref T args)
    {
        _dirty = true;
    }

    private void OnPylonAnchor(Entity<ST14EnergyPylonComponent> ent, ref AnchorStateChangedEvent args)
    {
        _dirty = true;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _links.Clear();
        _nextHit.Clear();
        _dirty = true;
    }

    private void OnPylonExamined(Entity<ST14EnergyPylonComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!Transform(ent).Anchored)
            args.PushMarkup(Loc.GetString("st14-energy-pylon-examine-unanchored"));
        else if (!ent.Comp.Powered)
            args.PushMarkup(Loc.GetString("st14-energy-pylon-examine-unpowered"));
        else
            args.PushMarkup(Loc.GetString("st14-energy-pylon-examine-powered", ("links", ent.Comp.LinkCount)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (_dirty)
        {
            _dirty = false;
            RebuildLinks();
        }

        UpdatePower(now);
        UpdateZones(now);
        UpdatePylons(now);
    }

    /// <summary>
    /// Whether the two pylons are linked
    /// </summary>
    public bool AreLinked(EntityUid a, EntityUid b)
    {
        return FindLink(a, b) != null;
    }

    /// <summary>
    /// Whether the link between the two pylons is burning right now
    /// </summary>
    public bool IsLinkActive(EntityUid a, EntityUid b)
    {
        return FindLink(a, b)?.Active ?? false;
    }

    private FenceLink? FindLink(EntityUid a, EntityUid b)
    {
        foreach (var link in _links)
        {
            if (link.A == a && link.B == b || link.A == b && link.B == a)
                return link;
        }

        return null;
    }

    /// <summary>
    /// Greedy pairing: every pair in range, nearest first, taken while both pylons have a free link.
    /// Links that survive keep their state
    /// </summary>
    private void RebuildLinks()
    {
        _pylons.Clear();
        var query = EntityQueryEnumerator<ST14EnergyPylonComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pylon, out var xform))
        {
            if (!xform.Anchored || xform.GridUid == null || TerminatingOrDeleted(uid) || Paused(uid))
                continue;

            _pylons.Add((uid, pylon, xform.MapID, _transform.GetWorldPosition(xform)));
        }

        // Stable order, so the pairing does not depend on the order of the entity query
        _pylons.Sort((x, y) => x.Uid.CompareTo(y.Uid));

        _pairs.Clear();
        for (var i = 0; i < _pylons.Count; i++)
        {
            for (var j = i + 1; j < _pylons.Count; j++)
            {
                var a = _pylons[i];
                var b = _pylons[j];
                if (a.Map != b.Map)
                    continue;

                var dist = (a.Pos - b.Pos).Length();
                if (dist > MathF.Min(a.Comp.LinkRange, b.Comp.LinkRange) + 0.01f)
                    continue;

                _pairs.Add((dist, i, j));
            }
        }

        _pairs.Sort((x, y) =>
        {
            var byDist = x.Dist.CompareTo(y.Dist);
            if (byDist != 0)
                return byDist;

            var byI = x.I.CompareTo(y.I);
            return byI != 0 ? byI : x.J.CompareTo(y.J);
        });

        var old = new List<FenceLink>(_links);
        _links.Clear();
        _linkCounts.Clear();

        foreach (var (_, i, j) in _pairs)
        {
            var a = _pylons[i];
            var b = _pylons[j];
            var countA = _linkCounts.GetValueOrDefault(a.Uid);
            var countB = _linkCounts.GetValueOrDefault(b.Uid);
            if (countA >= a.Comp.MaxLinks || countB >= b.Comp.MaxLinks)
                continue;

            _linkCounts[a.Uid] = countA + 1;
            _linkCounts[b.Uid] = countB + 1;

            var link = old.Find(l => l.A == a.Uid && l.B == b.Uid) ?? new FenceLink { A = a.Uid, B = b.Uid };
            _links.Add(link);
        }

        var pylons = EntityQueryEnumerator<ST14EnergyPylonComponent>();
        while (pylons.MoveNext(out var uid, out var pylon))
        {
            pylon.LinkCount = _linkCounts.GetValueOrDefault(uid);
        }
    }

    /// <summary>
    /// A pylon counts as powered only after it has had power for <see cref="ST14EnergyPylonComponent.PowerUpDelay"/>
    /// </summary>
    private void UpdatePower(TimeSpan now)
    {
        var query = EntityQueryEnumerator<ST14EnergyPylonComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pylon, out var receiver, out var xform))
        {
            // Pylons on a map that is not running yet (being built or saved) are left as spawned
            if (Paused(uid))
                continue;

            var raw = xform.Anchored && _receiver.IsPowered(uid, receiver);
            if (!raw)
            {
                pylon.PoweredSince = null;
                pylon.Powered = false;
                continue;
            }

            pylon.PoweredSince ??= now;
            pylon.Powered = now - pylon.PoweredSince.Value >= pylon.PowerUpDelay;
        }
    }

    /// <summary>
    /// Load on the power net, sprite state, light, hum and the idle sparks of every pylon
    /// </summary>
    private void UpdatePylons(TimeSpan now)
    {
        _activeCounts.Clear();
        _wrappedCounts.Clear();
        foreach (var link in _links)
        {
            if (!link.Active)
                continue;

            _activeCounts[link.A] = _activeCounts.GetValueOrDefault(link.A) + 1;
            _activeCounts[link.B] = _activeCounts.GetValueOrDefault(link.B) + 1;
            _wrappedCounts[link.A] = _wrappedCounts.GetValueOrDefault(link.A) + link.Wrapped.Count;
            _wrappedCounts[link.B] = _wrappedCounts.GetValueOrDefault(link.B) + link.Wrapped.Count;
        }

        var query = EntityQueryEnumerator<ST14EnergyPylonComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var pylon, out var receiver, out var xform))
        {
            // Pylons on a map that is not running yet (being built or saved) are left as spawned
            if (Paused(uid))
                continue;

            var active = _activeCounts.GetValueOrDefault(uid);
            var wrapped = _wrappedCounts.GetValueOrDefault(uid);

            var load = pylon.IdleLoad + active * pylon.ActiveLoadPerLink + wrapped * pylon.PerBugLoad;
            if (!MathHelper.CloseTo(receiver.Load, load))
                _receiver.SetLoad((uid, receiver), load);

            var state = !xform.Anchored ? ST14EnergyPylonState.Unanchored
                : !pylon.Powered ? ST14EnergyPylonState.NoPower
                : active > 0 ? ST14EnergyPylonState.Active
                : ST14EnergyPylonState.Idle;
            _appearance.SetData(uid, ST14EnergyPylonVisuals.State, state);

            _light.SetEnabled(uid, pylon.Powered);
            if (pylon.Powered)
                _light.SetEnergy(uid, active > 0 ? pylon.ActiveLightEnergy : pylon.IdleLightEnergy);

            _ambient.SetAmbience(uid, active > 0);

            if (!pylon.Powered || active > 0 || pylon.IdleSparkEffect is not { } spark)
                continue;

            if (pylon.NextIdleSpark < now - pylon.IdleSparkMaxDelay)
            {
                pylon.NextIdleSpark = now + RandomSparkDelay(pylon);
                continue;
            }

            if (now < pylon.NextIdleSpark)
                continue;

            pylon.NextIdleSpark = now + RandomSparkDelay(pylon);
            Spawn(spark, xform.Coordinates.Offset(pylon.ArcOffset));
        }
    }

    private TimeSpan RandomSparkDelay(ST14EnergyPylonComponent pylon)
    {
        return _random.Next(pylon.IdleSparkMinDelay, pylon.IdleSparkMaxDelay);
    }
}
