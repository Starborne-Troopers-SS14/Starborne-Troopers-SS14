/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.DoAfter;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Getting into and out of a trench carefully, with Alt+click: on the opening of a trench next to you from the ground
/// you get down into it, on the ground at the edge of the trench you stand in you get up onto it. A short wait, no
/// fall. Bound before the stock Alt+click, it only takes the clicks that are one of these two; the rest (an entity's
/// verbs) go on as before.
/// </summary>
public sealed partial class ST14TrenchSystem
{
    /// <summary>
    /// How far from the soldier the tile can be, from the middle of one to the point clicked
    /// </summary>
    private const float ClimbReach = 1.6f;

    private static readonly TimeSpan ClimbDownTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ClimbUpTime = TimeSpan.FromSeconds(1.5);

    private void InitializeClimb()
    {
        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.AltActivateItemInWorld,
                new PointerInputCmdHandler(OnAltClick, outsidePrediction: true),
                typeof(SharedInteractionSystem))
            .Register<ST14TrenchSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<ST14TrenchSystem>();
    }

    private bool OnAltClick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } user || !coords.IsValid(EntityManager))
            return false;

        // something with verbs of its own was clicked (a person, a crate): that goes on as an Alt+click does
        if (uid.IsValid() && Exists(uid) && !HasComp<ST14ClimbUpComponent>(uid))
            return false;

        if (!TryGetClimb(user, coords, out var target, out var down))
            return false;

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, down ? ClimbDownTime : ClimbUpTime,
            new ST14TrenchClimbDoAfterEvent(GetNetCoordinates(target), down), user)
        {
            BreakOnMove = true,
            BlockDuplicate = true,
        });

        return true;
    }

    /// <summary>
    /// The tile <paramref name="user"/> gets onto at <paramref name="coords"/>: down into the opening of a trench from
    /// the ground, or up onto the ground from the trench below it
    /// </summary>
    public bool TryGetClimb(EntityUid user, EntityCoordinates coords, out EntityCoordinates target, out bool down)
    {
        target = default;
        down = false;

        var userXform = Transform(user);
        if (userXform.MapUid is not { } map || !TryComp<ST14ZMapComponent>(map, out var zMap) ||
            _transform.GetMap(coords) != map)
        {
            return false;
        }

        var point = _transform.ToMapCoordinates(coords).Position;
        var tile = new Vector2i((int) MathF.Floor(point.X), (int) MathF.Floor(point.Y));
        var middle = tile + new Vector2(0.5f);
        var here = _transform.GetWorldPosition(userXform);
        var mine = new Vector2i((int) MathF.Floor(here.X), (int) MathF.Floor(here.Y));
        if (tile == mine || (middle - here).Length() > ClimbReach)
            return false;

        if (zMap.Depth == 0)
        {
            // from the ground: an open trench (not under a roof) with its floor below
            if (!_zLevels.TryMapDown((map, zMap), out var below) || !_zLevels.TryGetLayerGrid(map, out var surface) ||
                !_zLevels.TryGetLayerGrid(below, out var floor) || IsGround(surface.Value, tile) ||
                !IsGround(floor.Value, tile) || _roofs.TryGetRoofEntity(map, tile, out _) || IsBlocked(floor.Value, tile))
            {
                return false;
            }

            target = new EntityCoordinates(map, middle);
            down = true;
            return true;
        }

        if (zMap.Depth == -1)
        {
            // from a trench: the ground over the edge of it, free to stand on
            if (!_zLevels.TryMapUp((map, zMap), out var above) || !_zLevels.TryGetLayerGrid(above, out var surface) ||
                !IsGround(surface.Value, tile) || IsBlocked(surface.Value, tile) ||
                _roofs.TryGetRoofEntity(above, tile, out _))
            {
                return false;
            }

            target = new EntityCoordinates(map, middle);
            down = false;
            return true;
        }

        return false;
    }

    [SubscribeLocalEvent]
    private void OnTrenchClimbDone(Entity<Content.Shared.Mobs.Components.MobStateComponent> ent, ref ST14TrenchClimbDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var location = GetCoordinates(args.Location);

        // still the same climb: nothing dug, filled or built there meanwhile
        if (!TryGetClimb(args.User, location, out var target, out var down) || down != args.Down)
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-climb-blocked"), args.User, args.User);
            return;
        }

        _transform.SetWorldPosition(args.User, _transform.ToMapCoordinates(target).Position);
        _zLevels.TryMove(args.User, down ? -1 : 1);
    }
}
