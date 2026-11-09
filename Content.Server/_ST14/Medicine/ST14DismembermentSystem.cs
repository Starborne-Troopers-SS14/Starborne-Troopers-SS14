using System.Linq;
using System.Numerics;
using Content.Server._ST14.Blood;
using Content.Server.Chat.Systems;
using Content.Shared._ST14.Medicine;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Camera;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Server.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// Heavy hits tear arms and legs off a badly hurt body. It is meant to be seen and heard: the limb flies off,
/// blood sprays away from the hit, the bone cracks, the victim screams, falls and shakes, its screen goes white and
/// the ears ring, the screen jumps for everyone near, and the stump pumps blood until a tourniquet stops it
/// </summary>
public sealed partial class ST14DismembermentSystem : EntitySystem
{
    private static readonly ProtoId<NpcFactionPrototype> Hive = "Xeno";

    /// <summary>
    /// The limbs that come off, each with the key of its name
    /// </summary>
    private static readonly (ST14BodyZone Zone, string Name)[] Limbs =
    {
        (ST14BodyZone.ArmLeft, "st14-limb-arm-left"),
        (ST14BodyZone.ArmRight, "st14-limb-arm-right"),
        (ST14BodyZone.LegLeft, "st14-limb-leg-left"),
        (ST14BodyZone.LegRight, "st14-limb-leg-right"),
    };

    private const float WitnessRange = 6f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DetachableOrganSystem _detach = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedCameraRecoilSystem _camera = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14BloodDecalsSystem _blood = default!;
    [Dependency] private ST14MissingLegsSystem _legs = default!;
    [Dependency] private ST14WoundSystem _wounds = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    private readonly HashSet<Entity<ActorComponent>> _witnesses = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ST14DismemberableComponent, DamageDealtEvent>(OnDamage);
    }

    private void OnDamage(Entity<ST14DismemberableComponent> ent, ref DamageDealtEvent args)
    {
        if (_timing.CurTime < ent.Comp.NextAllowed || _mobState.IsDead(ent))
            return;

        var hit = 0f;
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            if (value > 0 && ent.Comp.DamageWeights.TryGetValue(type, out var weight))
                hit += value.Float() * weight;
        }

        if (args.Origin is { } origin && _faction.IsMember(origin, Hive))
            hit *= ent.Comp.HiveMultiplier;

        if (hit < ent.Comp.HitThreshold || _damageable.GetTotalDamage(ent.Owner).Float() < ent.Comp.WoundThreshold)
            return;

        var chance = MathF.Min(ent.Comp.MaxChance, (hit - ent.Comp.HitThreshold + 1f) * ent.Comp.ChancePerPoint);
        if (!_random.Prob(chance))
            return;

        TearOff(ent, args.Origin);
    }

    /// <summary>
    /// The limb organs still on the body, with the zone each one is
    /// </summary>
    private List<(Entity<OrganComponent, DetachableOrganComponent> Organ, ST14BodyZone Zone, string Name)> LimbsOf(EntityUid body)
    {
        var limbs = new List<(Entity<OrganComponent, DetachableOrganComponent>, ST14BodyZone, string)>();
        foreach (var organ in _body.EnumerateOrgans<DetachableOrganComponent>(body))
        {
            foreach (var (zone, name) in Limbs)
            {
                if (ST14Zones.Category(zone) == organ.Comp1.Category?.Id)
                    limbs.Add((organ, zone, name));
            }
        }

        return limbs;
    }

    /// <summary>
    /// Tears a random arm or leg off the body, with everything that goes with it
    /// </summary>
    public bool TearOff(Entity<ST14DismemberableComponent> ent, EntityUid? origin, ST14BodyZone? only = null)
    {
        var limbs = LimbsOf(ent);
        if (limbs.Count == 0)
            return false;

        if (only is { } wanted)
            limbs = limbs.Where(l => l.Zone == wanted).ToList();

        if (limbs.Count == 0)
            return false;

        var (organ, zone, nameKey) = _random.Pick(limbs);
        if (_detach.Detach((organ.Owner, organ.Comp2)) is not { } limb)
            return false;

        ent.Comp.NextAllowed = _timing.CurTime + ent.Comp.Cooldown;
        var away = AwayFrom(ent, origin);

        // the limb flies off away from the hit
        if (!_throwing.TryThrow(limb, away * _random.NextFloat(1.5f, 3f), 6f, playSound: false))
            _transform.SetWorldPosition(limb, _transform.GetWorldPosition(ent) + away * 1.2f);

        // the sound of it: flesh tearing and bone giving way
        _audio.PlayPvs(ent.Comp.TearSound, ent);
        _audio.PlayPvs(ent.Comp.CrackSound, ent);

        // blood: a fan of streaks away from the hit and a pool, then the stump keeps pumping
        _blood.Gush(ent, origin, 7);
        _wounds.LoseLimb(ent, zone, true);

        var name = Loc.GetString(nameKey);
        _popup.PopupEntity(Loc.GetString("st14-limb-torn-self", ("limb", name.ToUpperInvariant())),
            Loc.GetString("st14-limb-torn-others", ("victim", ent.Owner), ("limbOf", Loc.GetString(nameKey + "-of"))),
            ent, ent, PopupType.LargeCaution);

        // the victim goes down shaking and screaming, white before the eyes and ringing in the ears
        if (!_mobState.IsDead(ent))
        {
            _chat.TryEmoteWithChat(ent, ent.Comp.Scream, ignoreActionBlocker: true, forceEmote: true);
            _stun.TryKnockdown(ent.Owner, ent.Comp.Knockdown, force: true);
            _jitter.DoJitter(ent, ent.Comp.Shock, true, 18f, 9f);
            if (TryComp<ActorComponent>(ent, out var actor))
                RaiseNetworkEvent(new ST14MedicalShockEvent(1f), actor.PlayerSession);
        }

        Flinch(ent, away);

        _legs.Refresh(ent.Owner);

        return true;
    }

    /// <summary>
    /// A limb left too long under a tourniquet dies and falls away. Nothing to save, a prosthesis takes its place
    /// </summary>
    public bool Necrotize(EntityUid body, ST14BodyZone zone)
    {
        var limbs = LimbsOf(body).Where(l => l.Zone == zone).ToList();
        if (limbs.Count == 0 || _detach.Detach((limbs[0].Organ.Owner, limbs[0].Organ.Comp2)) is not { } dead)
            return false;

        Del(dead);
        _wounds.LoseLimb(body, zone, false);
        _legs.Refresh(body);

        return true;
    }

    /// <summary>
    /// The way from the one who hit to the victim, any way when nobody did
    /// </summary>
    private Vector2 AwayFrom(EntityUid target, EntityUid? origin)
    {
        if (origin is { } source && source != target && !TerminatingOrDeleted(source))
        {
            var delta = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(source);
            if (delta.LengthSquared() > 0.0001f)
                return Vector2.Normalize(delta);
        }

        return _random.NextAngle().ToVec();
    }

    /// <summary>
    /// The screen jumps hard for the victim and less for everyone who saw it close
    /// </summary>
    private void Flinch(EntityUid victim, Vector2 away)
    {
        _camera.KickCamera(victim, away * 1.4f);

        _witnesses.Clear();
        _lookup.GetEntitiesInRange(Transform(victim).Coordinates, WitnessRange, _witnesses);
        foreach (var witness in _witnesses)
        {
            if (witness.Owner == victim)
                continue;

            var distance = (_transform.GetWorldPosition(witness) - _transform.GetWorldPosition(victim)).Length();
            _camera.KickCamera(witness, _random.NextAngle().ToVec() * (0.5f * (1f - distance / WitnessRange) + 0.1f));
        }
    }
}
