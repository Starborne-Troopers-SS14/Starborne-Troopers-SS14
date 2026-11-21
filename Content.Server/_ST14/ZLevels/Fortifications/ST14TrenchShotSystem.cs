/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Server._ST14.ZLevels.Core;
using Content.Server._ST14.ZLevels.Roof;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Damage.Systems;
using Content.Server.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Shooting from and into trenches, the Foxhole way.
/// <list type="bullet">
/// <item>A soldier standing in an open trench (nothing over their head on the level above) fires over its edge: the
/// bullet flies on the level above.</item>
/// <item>Fire aimed at an open trench goes into it: the bullet flies on the surface up to the spot under the cursor and
/// comes down there. Whoever is under the cursor in the trench is hit for sure, standing or lying; aimed at an empty
/// spot of the trench the bullet is lost in its floor. Fire aimed past a trench flies over it. A bunker roof keeps both
/// out.</item>
/// </list>
/// </summary>
public sealed partial class ST14TrenchShotSystem : EntitySystem
{
    /// <summary>
    /// How close to the cursor someone in the trench must be to be hit: the cursor on them
    /// </summary>
    public const float AimRadius = 0.5f;

    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;
    [Dependency] private ST14ZLevelsRoofSystem _roofs = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private GunSystem _guns = default!;
    [Dependency] private ColorFlashEffectSystem _color = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    /// <summary>
    /// Bullets aimed into a trench: where they come down, on which level
    /// </summary>
    private readonly Dictionary<EntityUid, (EntityUid Level, Vector2 Aim)> _aimed = new();
    private readonly List<EntityUid> _done = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, AmmoShotEvent>(OnShot);
    }

    private void OnShot(Entity<GunComponent> gun, ref AmmoShotEvent args)
    {
        Vector2? aim = gun.Comp.ShootCoordinates is { } coords && coords.IsValid(EntityManager)
            ? _transform.ToMapCoordinates(coords).Position
            : null;

        foreach (var projectile in args.FiredProjectiles)
        {
            if (TryLiftShot(projectile, aim))
                continue;

            if (aim is { } point)
                AimIntoTrench(projectile, point);
        }
    }

    /// <summary>
    /// Moves a bullet just fired from an open trench to the level above, keeping its flight. Fire aimed along the
    /// trench line - at a spot of it dug out of the ground, open or under a dug-out roof - stays down in it, so it flies
    /// along the trench and in through the door of a dug-out
    /// </summary>
    public bool TryLiftShot(EntityUid projectile, Vector2? aim = null)
    {
        var xform = Transform(projectile);

        // only out of a trench: from the surface the level above is the open sky and the bullet must stay down
        if (xform.MapUid is not { } map || !TryComp<ST14ZMapComponent>(map, out var zMap) || zMap.Depth >= 0 ||
            !_zLevels.TryMapUp(map, out var above))
        {
            return false;
        }

        var position = _transform.GetWorldPosition(xform);
        if (!IsOpen(above, position))
            return false;

        if (aim is { } target && !HasGround(above, target))
            return false;

        MoveKeepingFlight(projectile, xform, above, position);
        return true;
    }

    /// <summary>
    /// A bullet aimed at <paramref name="aim"/>: if that spot is an open trench below the bullet's level, the bullet
    /// comes down into it there
    /// </summary>
    public bool AimIntoTrench(EntityUid projectile, Vector2 aim)
    {
        var xform = Transform(projectile);
        if (xform.MapUid is not { } map || !HasComp<ST14ZMapComponent>(map) ||
            !_zLevels.TryMapDown(map, out var below) || !IsOpen(map, aim))
        {
            return false;
        }

        // the floor of the trench must be there to fly along
        if (!_zLevels.TryGetLayerGrid(below, out var floor) ||
            !_map.TryGetTileRef(floor.Value, floor.Value.Comp, _map.WorldToTile(floor.Value, floor.Value.Comp, aim), out var floorRef) ||
            floorRef.Tile.IsEmpty)
        {
            return false;
        }

        _aimed[projectile] = (map, aim);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_aimed.Count == 0)
            return;

        _done.Clear();
        foreach (var (uid, (level, aim)) in _aimed)
        {
            if (TerminatingOrDeleted(uid) || !TryComp<PhysicsComponent>(uid, out var physics))
            {
                _done.Add(uid);
                continue;
            }

            var xform = Transform(uid);
            var velocity = physics.LinearVelocity;
            if (xform.MapUid != level || velocity.LengthSquared() < 0.01f)
            {
                _done.Add(uid);
                continue;
            }

            // not there yet: the aimed spot is still ahead
            var dir = Vector2.Normalize(velocity);
            var position = _transform.GetWorldPosition(xform);
            if (Vector2.Dot(aim - position, dir) > 0f)
                continue;

            _done.Add(uid);
            if (_zLevels.TryMapDown(level, out var below))
                ComeDown(uid, below, aim);
        }

        foreach (var uid in _done)
        {
            _aimed.Remove(uid);
        }
    }

    /// <summary>
    /// A bullet aimed into a trench reaches the aimed spot: whoever is under the cursor down there is hit, standing or
    /// lying (a bullet on its own flies over the lying, an aimed one does not); nobody there and it is lost in the floor
    /// </summary>
    private void ComeDown(EntityUid bullet, EntityUid below, Vector2 aim)
    {
        if (!TryComp<ProjectileComponent>(bullet, out var projectile))
        {
            QueueDel(bullet);
            return;
        }

        _mobs.Clear();
        _lookup.GetEntitiesInRange(new MapCoordinates(aim, Transform(below).MapID), AimRadius, _mobs);

        // the one nearest the cursor, the living first
        EntityUid? victim = null;
        var best = float.MaxValue;
        foreach (var mob in _mobs)
        {
            if (mob.Owner == projectile.Shooter)
                continue;

            var distance = (_transform.GetWorldPosition(mob) - aim).LengthSquared();
            if (!_mobState.IsDead(mob, mob.Comp))
                distance -= 100f;

            if (distance < best)
            {
                best = distance;
                victim = mob.Owner;
            }
        }

        if (victim is { } target)
            Hit((bullet, projectile), target);

        QueueDel(bullet);
    }

    /// <summary>
    /// The hit of <see cref="Content.Server.Projectiles.ProjectileSystem"/>: the same events, damage, flash and sound
    /// </summary>
    private void Hit(Entity<ProjectileComponent> bullet, EntityUid target)
    {
        var before = new BeforeProjectileHitEvent(bullet.Comp.Damage, target, bullet.Comp.Shooter);
        RaiseLocalEvent(bullet, ref before);
        var hit = new ProjectileHitEvent(before.Damage * _damageable.UniversalProjectileDamageModifier, target, bullet.Comp.Shooter);
        RaiseLocalEvent(bullet, ref hit);

        _damageable.TryChangeDamage(target, hit.Damage, out var damage, bullet.Comp.IgnoreResistances, origin: bullet.Comp.Shooter);
        bullet.Comp.ProjectileSpent = true;

        if (Deleted(target))
            return;

        _color.RaiseEffect(Color.Red, new List<EntityUid> { target }, Filter.Pvs(target, entityManager: EntityManager));
        _guns.PlayImpactSound(target, damage, bullet.Comp.SoundHit, bullet.Comp.ForceSound);
    }

    /// <summary>
    /// The level has ground on this world tile: it is not dug out
    /// </summary>
    private bool HasGround(EntityUid level, Vector2 position)
    {
        if (!_zLevels.TryGetLayerGrid(level, out var grid))
            return false;

        var index = _map.WorldToTile(grid.Value, grid.Value.Comp, position);
        return _map.TryGetTileRef(grid.Value, grid.Value.Comp, index, out var tileRef) && !tileRef.Tile.IsEmpty;
    }

    /// <summary>
    /// Nothing on this world tile of the level: no ground, no bunker roof
    /// </summary>
    private bool IsOpen(EntityUid level, Vector2 position)
    {
        var tile = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
        if (_roofs.TryGetRoofEntity(level, tile, out _))
            return false;

        if (!_zLevels.TryGetLayerGrid(level, out var grid))
            return true;

        var index = _map.WorldToTile(grid.Value, grid.Value.Comp, position);
        return !_map.TryGetTileRef(grid.Value, grid.Value.Comp, index, out var tileRef) || tileRef.Tile.IsEmpty;
    }

    public void MoveKeepingFlight(EntityUid uid, TransformComponent xform, EntityUid level, Vector2 position)
    {
        var velocity = _physics.GetMapLinearVelocity(uid);
        var rotation = _transform.GetWorldRotation(xform);

        _transform.SetMapCoordinates((uid, xform), new MapCoordinates(position, Transform(level).MapID));
        _transform.SetWorldRotation(uid, rotation);
        _physics.SetLinearVelocity(uid, velocity);
    }
}
