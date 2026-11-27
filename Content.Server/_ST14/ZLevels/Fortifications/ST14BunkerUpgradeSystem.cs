/*
 * Starborne Troopers bunker upgrades on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Server._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.DoAfter;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// The upgrades of a bunker (ST14BunkerUpgradeComponents.cs), built by the engineer. As in Foxhole they stand on the
/// roof of the bunker, over its middle: that is what the bunker looks like from outside, and the inside stays free.
/// <list type="bullet">
/// <item>Built inside, an upgrade moves up onto the roof; what it puts inside (the supply crate of the base) goes in a
/// corner.</item>
/// <item>A garrison puts its gun in the middle of the roof (ST14BunkerGarrisonGunSystem fires it); its bullets go out
/// through the roofs.</item>
/// <item>The observation bunker: whoever is inside can zoom out further, and is given their zoom back on leaving.</item>
/// <item>The ramp and the ladder put their two ends in place: Alt+click on one end goes to the other.</item>
/// <item>A firing slit takes the place of the earth wall in the middle of a side.</item>
/// </list>
/// The forward base is ST14SectorSpawnSystem (troopers come back to life in it) and its supply crate.
/// </summary>
public sealed partial class ST14BunkerUpgradeSystem : EntitySystem
{
    private static readonly TimeSpan ObserveInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedContentEyeSystem _eye = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ST14ZLevelsSystem _zLevels = default!;
    [Dependency] private ST14TrenchShotSystem _shots = default!;

    private TimeSpan _nextObserve;
    private readonly HashSet<EntityUid> _inside = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14BunkerUpgradeComponent, MapInitEvent>(OnUpgradeInit);
        SubscribeLocalEvent<ST14BunkerUpgradeComponent, ComponentShutdown>(OnUpgradeShutdown);
        SubscribeLocalEvent<ST14BunkerLadderComponent, MapInitEvent>(OnLadderInit);
        SubscribeLocalEvent<ST14BunkerLadderComponent, ComponentShutdown>(OnLadderShutdown);
        SubscribeLocalEvent<ST14BunkerPassageComponent, GetVerbsEvent<AlternativeVerb>>(OnPassageVerbs);
        SubscribeLocalEvent<ST14BunkerPassageComponent, ST14BunkerPassageDoAfterEvent>(OnPassageDone);
        SubscribeLocalEvent<ST14BunkerRoofComponent, PreventCollideEvent>(OnRoofPreventCollide);
        SubscribeLocalEvent<ST14BunkerSlitComponent, MapInitEvent>(OnSlitInit);
        SubscribeLocalEvent<ST14BunkerSlitComponent, StartCollideEvent>(OnSlitCollide);
    }

    /// <summary>
    /// The upgrade goes on the roof over the middle of its bunker, facing the way it was built, and puts what it brings
    /// in place: the gun of a garrison, the ends of a ramp, the crate inside
    /// </summary>
    private void OnUpgradeInit(Entity<ST14BunkerUpgradeComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (!ST14Dugouts.TryGetAt(EntityManager, xform.Coordinates, out var surface, out var center, out var size))
            return;

        var rotation = xform.LocalRotation;
        var middle = center + new Vector2(0.5f);
        _transform.SetCoordinates(ent, xform, new EntityCoordinates(surface, middle), rotation);

        var half = size / 2;
        _zLevels.TryMapDown(surface, out var floor);

        if (ent.Comp.Inside is { } inside && floor.Owner.IsValid())
        {
            // the north-west corner: out of the way of the doors in the middles of the sides
            ent.Comp.InsideEntity = Spawn(inside, new EntityCoordinates(floor, center + new Vector2(-half + 0.5f, half + 0.5f)));
        }

        if (TryComp<ST14BunkerGarrisonComponent>(ent, out var garrison))
        {
            var gun = Spawn(garrison.Turret, new EntityCoordinates(surface, middle));
            _transform.SetLocalRotation(gun, rotation);
            if (TryComp<ST14BunkerGarrisonGunComponent>(gun, out var gunComp))
                gunComp.Facing = rotation;

            garrison.Gun = gun;
        }

        if (TryComp<ST14BunkerRampComponent>(ent, out var ramp) && floor.Owner.IsValid())
        {
            // the ramp goes down over the side it faces: from the ground past its middle to the floor just inside
            var side = rotation.GetCardinalDir().ToIntVec();
            var outside = Spawn(ramp.OutsideProto, new EntityCoordinates(surface, center + side * (half + 1) + new Vector2(0.5f)));
            var insideEnd = Spawn(ramp.InsideProto, new EntityCoordinates(floor, center + side * half + new Vector2(0.5f)));
            _transform.SetLocalRotation(outside, rotation);
            _transform.SetLocalRotation(insideEnd, rotation);
            Link(outside, insideEnd);
            ramp.Outside = outside;
            ramp.Inside = insideEnd;
        }
    }

    private void OnUpgradeShutdown(Entity<ST14BunkerUpgradeComponent> ent, ref ComponentShutdown args)
    {
        Delete(ent.Comp.InsideEntity);
        if (TryComp<ST14BunkerGarrisonComponent>(ent, out var garrison))
            Delete(garrison.Gun);

        if (TryComp<ST14BunkerRampComponent>(ent, out var ramp))
        {
            Delete(ramp.Outside);
            Delete(ramp.Inside);
        }
    }

    /// <summary>
    /// A ladder up through the roof: its top goes on the roof right over its foot
    /// </summary>
    private void OnLadderInit(Entity<ST14BunkerLadderComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (xform.MapUid is not { } floor || !_zLevels.TryMapUp(floor, out var above))
            return;

        var top = Spawn(ent.Comp.TopProto, new EntityCoordinates(above, _transform.GetWorldPosition(xform)));
        Link(ent, top);
        ent.Comp.Top = top;
    }

    private void OnLadderShutdown(Entity<ST14BunkerLadderComponent> ent, ref ComponentShutdown args)
    {
        Delete(ent.Comp.Top);
    }

    private void Link(EntityUid a, EntityUid b)
    {
        if (TryComp<ST14BunkerPassageComponent>(a, out var first))
            first.Other = b;

        if (TryComp<ST14BunkerPassageComponent>(b, out var second))
            second.Other = a;
    }

    private void Delete(EntityUid? uid)
    {
        if (uid is { } some && !TerminatingOrDeleted(some))
            QueueDel(some);
    }

    private void OnPassageVerbs(Entity<ST14BunkerPassageComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Other == null)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.Verb),
            Priority = 3,
            Act = () => _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.Time,
                new ST14BunkerPassageDoAfterEvent(), ent, target: ent)
            {
                BreakOnMove = true,
            }),
        });
    }

    /// <summary>
    /// To the other end: on the level it stands on, where it stands
    /// </summary>
    private void OnPassageDone(Entity<ST14BunkerPassageComponent> ent, ref ST14BunkerPassageDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Other is not { } other || TerminatingOrDeleted(other))
            return;

        args.Handled = true;
        Go(args.User, other);
    }

    /// <summary>
    /// Moves <paramref name="user"/> to the end <paramref name="other"/> of a ramp or a ladder
    /// </summary>
    public void Go(EntityUid user, EntityUid other)
    {
        var otherXform = Transform(other);
        if (Transform(user).MapUid is not { } map || otherXform.MapUid is not { } otherMap)
            return;

        _transform.SetWorldPosition(user, _transform.GetWorldPosition(otherXform));
        if (map == otherMap)
            return;

        var offset = _zLevels.TryMapUp(map, out var above) && above.Owner == otherMap ? 1 : -1;
        if (!_zLevels.TryMove(user, offset))
            _popup.PopupEntity(Loc.GetString("st14-trench-climb-blocked"), user, user);
    }

    /// <summary>
    /// The bullets of a garrison go out through the roofs of the bunkers
    /// </summary>
    private void OnRoofPreventCollide(Entity<ST14BunkerRoofComponent> ent, ref PreventCollideEvent args)
    {
        if (TryComp<ProjectileComponent>(args.OtherEntity, out var projectile) &&
            HasComp<ST14BunkerGarrisonGunComponent>(projectile.Shooter))
        {
            args.Cancelled = true;
        }
    }

    /// <summary>
    /// The slit takes the place of the earth wall where it is built
    /// </summary>
    private void OnSlitInit(Entity<ST14BunkerSlitComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<Robust.Shared.Map.Components.MapGridComponent>(gridUid, out var grid))
            return;

        var walls = new List<EntityUid>();
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
        while (anchored.MoveNext(out var uid))
        {
            if (MetaData(uid.Value).EntityPrototype?.ID == ST14TrenchSystem.TrenchWall.Id)
                walls.Add(uid.Value);
        }

        foreach (var wall in walls)
        {
            Del(wall);
        }
    }

    /// <summary>
    /// A bullet fired out through the slit comes up over the ground there
    /// </summary>
    private void OnSlitCollide(Entity<ST14BunkerSlitComponent> ent, ref StartCollideEvent args)
    {
        if (!HasComp<ProjectileComponent>(args.OtherEntity))
            return;

        var xform = Transform(args.OtherEntity);
        if (xform.MapUid is not { } map || !_zLevels.TryMapUp(map, out var above))
            return;

        _shots.MoveKeepingFlight(args.OtherEntity, xform, above, _transform.GetWorldPosition(xform));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextObserve)
            return;

        _nextObserve = _timing.CurTime + ObserveInterval;
        UpdateObservers();
    }

    /// <summary>
    /// Whoever is inside a bunker with a radio tower on it can zoom out further; out of it, back to their own zoom
    /// </summary>
    private void UpdateObservers()
    {
        _inside.Clear();
        var bunkers = EntityQueryEnumerator<ST14BunkerObservationComponent, TransformComponent>();
        while (bunkers.MoveNext(out var bunker, out var observation, out var xform))
        {
            if (!ST14Dugouts.TryGetAt(EntityManager, xform.Coordinates, out var surface, out var center, out var size) ||
                !_zLevels.TryMapDown(surface, out var floorLevel))
            {
                continue;
            }

            var floor = floorLevel.Owner;
            var half = size / 2;
            var min = new Vector2(center.X - half, center.Y - half);
            var max = new Vector2(center.X + half + 1, center.Y + half + 1);
            var eyes = EntityQueryEnumerator<ContentEyeComponent, TransformComponent>();
            while (eyes.MoveNext(out var uid, out var eye, out var eyeXform))
            {
                if (eyeXform.MapUid != floor)
                    continue;

                var pos = _transform.GetWorldPosition(eyeXform);
                if (pos.X < min.X || pos.Y < min.Y || pos.X >= max.X || pos.Y >= max.Y)
                    continue;

                _inside.Add(uid);
                if (HasComp<ST14BunkerObserverComponent>(uid))
                    continue;

                var observer = AddComp<ST14BunkerObserverComponent>(uid);
                observer.MaxZoomBefore = eye.MaxZoom;
                observer.Bunker = bunker;
                _eye.SetMaxZoom(uid, Vector2.Max(eye.MaxZoom, observation.Zoom), eye);
                _eye.SetZoom(uid, observation.Zoom, eye: eye);
            }
        }

        var observers = EntityQueryEnumerator<ST14BunkerObserverComponent, ContentEyeComponent>();
        var leaving = new List<(EntityUid, ST14BunkerObserverComponent, ContentEyeComponent)>();
        while (observers.MoveNext(out var uid, out var observer, out var eye))
        {
            if (!_inside.Contains(uid))
                leaving.Add((uid, observer, eye));
        }

        foreach (var (uid, observer, eye) in leaving)
        {
            _eye.SetMaxZoom(uid, observer.MaxZoomBefore, eye);
            RemComp<ST14BunkerObserverComponent>(uid);
        }
    }
}
