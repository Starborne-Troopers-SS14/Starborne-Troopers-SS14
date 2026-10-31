using System.Numerics;
using Content.Server.Decals;
using Content.Shared._ST14.Blood;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Blood;

/// <summary>
/// Puts blood decals on the floor for mobs with <see cref="ST14BloodDecalsComponent"/>
/// </summary>
public sealed partial class ST14BloodDecalsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    // Blood sprites are 48 px, a decal is placed by its corner and turns around its middle
    private static readonly Vector2 DecalCenter = new(0.75f, 0.75f);

    // A single drip event never paints more than this, the rest of the blood is lost on the way
    private const int MaxDripsPerBleed = 4;

    private readonly Dictionary<string, List<string>> _decalsByTag = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14BloodDecalsComponent, DamageDealtEvent>(OnDamageDealt);
        SubscribeLocalEvent<ST14BloodDecalsComponent, ST14BleedOutEvent>(OnBleedOut);
        SubscribeLocalEvent<ST14BloodDecalsComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<DecalPrototype>())
            _decalsByTag.Clear();
    }

    private void OnDamageDealt(Entity<ST14BloodDecalsComponent> ent, ref DamageDealtEvent args)
    {
        if (_timing.CurTime < ent.Comp.NextSplatter)
            return;

        var strength = 0f;
        foreach (var (type, value) in args.Damage.DamageDict)
        {
            if (value > 0 && ent.Comp.SplatterDamage.TryGetValue(type, out var weight))
                strength += value.Float() * weight;
        }

        if (strength < ent.Comp.SplatterThreshold)
            return;

        ent.Comp.NextSplatter = _timing.CurTime + ent.Comp.SplatterCooldown;

        var direction = HitDirection(ent, args.Origin);
        Splatter(ent, direction, 0.45f);

        if (strength >= ent.Comp.HeavySplatterThreshold)
            Splatter(ent, direction, 1.1f);
    }

    private void OnBleedOut(Entity<ST14BloodDecalsComponent> ent, ref ST14BleedOutEvent args)
    {
        if (args.Burst)
        {
            for (var i = 0; i < ent.Comp.DeathPools; i++)
            {
                Place(ent, ent.Comp.PoolTag, _random.NextVector2(0.5f), _random.NextAngle());
            }

            for (var i = 0; i < 4; i++)
            {
                Splatter(ent, _random.NextAngle(), _random.NextFloat(0.4f, 1.2f));
            }

            ent.Comp.Lost = 0;
            return;
        }

        ent.Comp.Lost += args.Amount;

        var drips = 0;
        while (ent.Comp.Lost >= ent.Comp.DripVolume)
        {
            ent.Comp.Lost -= ent.Comp.DripVolume;

            if (drips++ < MaxDripsPerBleed)
                Place(ent, ent.Comp.DripTag, _random.NextVector2(ent.Comp.DripSpread), _random.NextAngle());
        }
    }

    private void OnMobStateChanged(Entity<ST14BloodDecalsComponent> ent, ref MobStateChangedEvent args)
    {
        var pools = args.NewMobState switch
        {
            MobState.Dead => ent.Comp.DeathPools,
            MobState.Critical => 1,
            _ => 0,
        };

        for (var i = 0; i < pools; i++)
        {
            Place(ent, ent.Comp.PoolTag, _random.NextVector2(0.25f), _random.NextAngle());
        }
    }

    /// <summary>
    /// The way the blood flies: away from whoever dealt the hit, or any way when nobody did
    /// </summary>
    private Angle HitDirection(EntityUid target, EntityUid? origin)
    {
        if (origin is { } source && source != target && !TerminatingOrDeleted(source))
        {
            var from = _transform.GetMapCoordinates(source);
            var to = _transform.GetMapCoordinates(target);
            var delta = to.Position - from.Position;

            if (from.MapId == to.MapId && delta.LengthSquared() > 0.0001f)
                return delta.ToWorldAngle() + _random.NextAngle(-0.35f, 0.35f);
        }

        return _random.NextAngle();
    }

    /// <summary>
    /// Throws one splatter in the given direction, its streaks pointing away from the mob
    /// </summary>
    private void Splatter(Entity<ST14BloodDecalsComponent> ent, Angle direction, float distance)
    {
        // Splatter sprites streak along +X, world angles start from -Y
        var rotation = direction - Angle.FromDegrees(90);
        var offset = rotation.ToVec() * distance + _random.NextVector2(0.15f);
        Place(ent, ent.Comp.SplatterTag, offset, rotation);
    }

    private void Place(EntityUid uid, string tag, Vector2 offset, Angle rotation)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid)
            return;

        var decals = GetDecals(tag);
        if (decals.Count == 0)
            return;

        var gridRotation = _transform.GetWorldRotation(grid);
        var local = _transform.WithEntityId(xform.Coordinates, grid).Position
                    + (-gridRotation).RotateVec(offset)
                    - DecalCenter;

        _decals.TryAddDecal(_random.Pick(decals),
            new EntityCoordinates(grid, local),
            out _,
            rotation: rotation - gridRotation,
            cleanable: true);
    }

    private List<string> GetDecals(string tag)
    {
        if (_decalsByTag.TryGetValue(tag, out var list))
            return list;

        list = new List<string>();
        foreach (var decal in _prototype.EnumeratePrototypes<DecalPrototype>())
        {
            if (decal.Tags.Contains(tag))
                list.Add(decal.ID);
        }

        _decalsByTag[tag] = list;
        return list;
    }
}
