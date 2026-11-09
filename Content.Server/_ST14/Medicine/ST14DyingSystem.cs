using Content.Shared._ST14.Medicine;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The fight for a downed person: the heart of <see cref="ST14DyingComponent"/>, and the time a dead one can still be brought back
/// </summary>
public sealed partial class ST14DyingSystem : EntitySystem
{
    private static readonly ProtoId<DamageTypePrototype> DyingDamage = "Asphyxiation";
    private static readonly ProtoId<DamageGroupPrototype> ReviveHealGroup = "Brute";
    private static readonly TimeSpan ReviveBeatDelay = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;

    private readonly List<Entity<ST14DyingComponent>> _due = new();
    private readonly List<EntityUid> _lost = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<ST14DyingComponent, TargetDefibrillatedEvent>(OnDefibrillated);
        SubscribeNetworkEvent<ST14HeartbeatPressEvent>(OnPress);
    }

    private void OnMobState(MobStateChangedEvent ev)
    {
        // bugs go straight from alive to dead and only people have a heart to fight for
        if (!HasComp<HumanoidProfileComponent>(ev.Target))
            return;

        switch (ev.NewMobState)
        {
            case MobState.Critical:
                RemComp<ST14RevivableComponent>(ev.Target);

                // a stopped heart has no beat to fight with: it needs a medic first
                if (TryComp<ST14WoundsComponent>(ev.Target, out var wounds) && wounds.Heart == ST14HeartState.Arrest)
                    break;

                StartFight(ev.Target);
                break;
            case MobState.Dead:
                RemComp<ST14DyingComponent>(ev.Target);
                if (!HasComp<UnrevivableComponent>(ev.Target))
                {
                    var revivable = EnsureComp<ST14RevivableComponent>(ev.Target);
                    revivable.LostAt = _timing.CurTime + revivable.Window;
                }
                break;
            default:
                RemComp<ST14DyingComponent>(ev.Target);
                RemComp<ST14RevivableComponent>(ev.Target);
                break;
        }
    }

    /// <summary>
    /// The downed body starts fighting for its heart, beat by beat
    /// </summary>
    public void StartFight(EntityUid uid)
    {
        var dying = EnsureComp<ST14DyingComponent>(uid);
        NextBeat((uid, dying));
    }

    private void OnDefibrillated(Entity<ST14DyingComponent> ent, ref TargetDefibrillatedEvent args)
    {
        ent.Comp.ReviveBeat = true;
        ent.Comp.Result = ST14BeatResult.None;
        ent.Comp.NextBeat = _timing.CurTime + ReviveBeatDelay;
        Dirty(ent);
    }

    private void OnPress(ST14HeartbeatPressEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } uid && TryComp<ST14DyingComponent>(uid, out var dying))
            Press((uid, dying));
    }

    /// <summary>
    /// Judges a press against the next beat. The first press of a beat counts, so mashing the key is a miss.
    /// The press arrives about when the predicting client drew it, no ping correction is needed on top of the wide window
    /// </summary>
    public void Press(Entity<ST14DyingComponent> ent)
    {
        if (ent.Comp.Result != ST14BeatResult.None)
            return;

        var window = ent.Comp.ReviveBeat ? ent.Comp.ReviveWindow : ent.Comp.Window;
        var off = Math.Abs((_timing.CurTime - ent.Comp.NextBeat).TotalSeconds);
        ent.Comp.Result = off <= window ? ST14BeatResult.Hit : ST14BeatResult.Miss;
        Dirty(ent);

        if (ent.Comp.Result == ST14BeatResult.Hit && TryComp<ActorComponent>(ent, out var actor))
            _audio.PlayGlobal(ent.Comp.HitSound, actor.PlayerSession);
    }

    private void NextBeat(Entity<ST14DyingComponent> ent)
    {
        var critical = _thresholds.GetThresholdForState(ent, MobState.Critical);
        var dead = _thresholds.GetThresholdForState(ent, MobState.Dead);
        var progress = dead > critical
            ? Math.Clamp((_damageable.GetTotalDamage(ent.Owner) - critical).Float() / (dead - critical).Float(), 0f, 1f)
            : 0f;

        ent.Comp.BeatInterval = MathHelper.Lerp(ent.Comp.FastestInterval, ent.Comp.SlowestInterval, progress);
        ent.Comp.NextBeat = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.BeatInterval);
        ent.Comp.Result = ST14BeatResult.None;
        ent.Comp.ReviveBeat = false;
        Dirty(ent);
    }

    private void Beat(Entity<ST14DyingComponent> ent)
    {
        if (ent.Comp.ReviveBeat)
        {
            if (ent.Comp.Result == ST14BeatResult.Hit)
                _damageable.HealDistributed(ent.Owner, -ent.Comp.ReviveHeal, ReviveHealGroup);
        }
        else
        {
            var multiplier = ent.Comp.Result switch
            {
                ST14BeatResult.Hit => ent.Comp.HitMultiplier,
                ST14BeatResult.Miss => ent.Comp.MissMultiplier,
                _ => 1f,
            };

            var damage = new DamageSpecifier(_prototype.Index(DyingDamage),
                FixedPoint2.New(ent.Comp.DamagePerSecond * ent.Comp.BeatInterval * multiplier));
            _damageable.TryChangeDamage(ent.Owner, damage, ignoreResistances: true, interruptsDoAfters: false, ignoreGlobalModifiers: true);
        }

        // the damage or the healing may have changed the state and taken the heart away
        if (TryComp<ST14DyingComponent>(ent, out var dying))
            NextBeat((ent, dying));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        _due.Clear();
        var dyingQuery = EntityQueryEnumerator<ST14DyingComponent>();
        while (dyingQuery.MoveNext(out var uid, out var dying))
        {
            var window = dying.ReviveBeat ? dying.ReviveWindow : dying.Window;
            if (now >= dying.NextBeat + TimeSpan.FromSeconds(window))
                _due.Add((uid, dying));
        }

        foreach (var ent in _due)
        {
            Beat(ent);
        }

        _lost.Clear();
        var revivableQuery = EntityQueryEnumerator<ST14RevivableComponent>();
        while (revivableQuery.MoveNext(out var uid, out var revivable))
        {
            if (now >= revivable.LostAt)
                _lost.Add(uid);
        }

        foreach (var uid in _lost)
        {
            RemComp<ST14RevivableComponent>(uid);
            var unrevivable = EnsureComp<UnrevivableComponent>(uid);
            unrevivable.ReasonMessage = "st14-medicine-too-late";
            Dirty(uid, unrevivable);
        }
    }
}
