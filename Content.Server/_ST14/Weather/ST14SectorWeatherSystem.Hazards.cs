using System.Numerics;
using Content.Server.Decals;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._ST14.Weather;
using Content.Shared.Camera;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._ST14.Weather;

public sealed partial class ST14SectorWeatherSystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private ExplosionSystem _explosion = default!;

    #region Strikes

    private void UpdateStrikes()
    {
        var now = _timing.CurTime;
        var hits = new List<(EntityUid, ST14WeatherStrikeComponent)>();
        var query = EntityQueryEnumerator<ST14WeatherStrikeComponent>();
        while (query.MoveNext(out var uid, out var strike))
        {
            if (strike.ImpactAt == TimeSpan.Zero)
            {
                strike.ImpactAt = now + strike.Delay;
                if (strike.FallSound != null)
                    _audio.PlayPvs(strike.FallSound, uid);
                continue;
            }

            if (now >= strike.ImpactAt)
                hits.Add((uid, strike));
        }

        foreach (var (uid, strike) in hits)
        {
            Impact(uid, strike);
            QueueDel(uid);
        }
    }

    private void Impact(EntityUid uid, ST14WeatherStrikeComponent strike)
    {
        var xform = Transform(uid);
        var coords = xform.Coordinates;
        var mapCoords = _transform.GetMapCoordinates(uid, xform);

        if (strike.ImpactSound != null)
            _audio.PlayPvs(strike.ImpactSound, coords);

        if (strike.Effect is { } effect)
            Spawn(effect, coords);

        if (strike.Flash && xform.MapUid is { } map)
        {
            _dayNight.Flash(map, TimeSpan.FromSeconds(0.12));
            RaiseNetworkEvent(new ST14WeatherFlashEvent(GetNetEntity(map), mapCoords.Position), MapFilter(map));
        }

        if (strike.Explosion is { } explosion)
        {
            // the sky does not dig holes in the planet: no tiles break
            _explosion.QueueExplosion(mapCoords, explosion, strike.ExplosionIntensity, strike.ExplosionSlope,
                strike.ExplosionMaxTile, null, tileBreakScale: 0f, maxTileBreak: 0, canCreateVacuum: false, addLog: false);
        }

        var someoneUnder = false;
        if (!strike.Damage.Empty || strike.Knockdown > TimeSpan.Zero)
        {
            foreach (var target in _lookup.GetEntitiesInRange<DamageableComponent>(coords, strike.Radius))
            {
                if (target.Owner == uid)
                    continue;

                if (!strike.Damage.Empty)
                    _damage.TryChangeDamage((target.Owner, target.Comp), strike.Damage);

                if (!HasComp<MobStateComponent>(target))
                    continue;

                someoneUnder = true;
                if (strike.Knockdown > TimeSpan.Zero)
                    _stun.TryKnockdown(target.Owner, strike.Knockdown);
            }
        }

        if (strike.ShakeRange > 0)
        {
            foreach (var mob in _lookup.GetEntitiesInRange<CameraRecoilComponent>(coords, strike.ShakeRange))
            {
                var away = _transform.GetWorldPosition(mob) - mapCoords.Position;
                var near = 1f - Math.Clamp(away.Length() / strike.ShakeRange, 0f, 1f);
                var push = away.LengthSquared() > 0.01f ? Vector2.Normalize(away) : _random.NextVector2();
                _recoil.KickCamera(mob, push * (0.3f + near * 1.2f));
            }
        }

        for (var i = 0; i < strike.DecalCount && strike.Decals.Count > 0; i++)
        {
            var offset = _random.NextVector2(strike.Radius);
            _decals.TryAddDecal(_random.Pick(strike.Decals), coords.Offset(offset - new Vector2(0.5f, 0.5f)), out _,
                rotation: _random.NextAngle(), cleanable: true);
        }

        if (strike.Leave is { } leave && !someoneUnder && _random.Prob(strike.LeaveChance) && FreeTile(xform))
            Spawn(leave, coords.SnapToGrid(EntityManager));
    }

    /// <summary>
    /// Nothing anchored stands on the tile: a boulder does not fall into a wall
    /// </summary>
    private bool FreeTile(TransformComponent xform)
    {
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var index = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, index);
        return !anchored.MoveNext(out _);
    }

    #endregion

}
