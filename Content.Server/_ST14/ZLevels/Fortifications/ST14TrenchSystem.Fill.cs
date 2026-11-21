/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Filling a trench back in with the spade (Foxhole: "bury"), a tile at a time: the ground over it comes back as the
/// ground round it, down below it is solid earth again (a wall where a trench still runs past it), and whoever and
/// whatever was in it comes up onto the ground. A bunker is not filled in.
/// </summary>
public sealed partial class ST14TrenchSystem
{
    private void StartFill(Entity<ST14TrenchDiggerComponent> digger, EntityUid user, EntityUid ground, Vector2i worldTile)
    {
        if (_roofs.TryGetRoofEntity(ground, worldTile, out _))
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-fill-roofed"), user, user);
            return;
        }

        var location = new EntityCoordinates(ground, worldTile + new Vector2(0.5f));
        _doAfter.TryStartDoAfter(new Content.Shared.DoAfter.DoAfterArgs(EntityManager, user, digger.Comp.DigTime,
            new ST14FillTrenchDoAfterEvent(GetNetCoordinates(location)), digger, used: digger)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnFillDone(Entity<ST14TrenchDiggerComponent> ent, ref ST14FillTrenchDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!TryGetGround(GetCoordinates(args.Location), out var ground, out var worldTile, out var dug) || !dug ||
            !FillTrench(ground, worldTile))
        {
            _popup.PopupEntity(Loc.GetString("st14-trench-not-dug"), args.User, args.User);
            return;
        }

        _popup.PopupEntity(Loc.GetString("st14-trench-filled"), args.User, args.User);
    }

    /// <summary>
    /// Fills in the trench under <paramref name="worldTile"/> of the surface <paramref name="ground"/>
    /// </summary>
    /// <returns>false if there is no trench there to fill in, or it is a bunker</returns>
    public bool FillTrench(EntityUid ground, Vector2i worldTile)
    {
        if (!_zLevels.TryMapDown(ground, out var below) ||
            !_zLevels.TryGetLayerGrid(ground, out var surfaceGrid) ||
            !_zLevels.TryGetLayerGrid(below, out var floorGrid) ||
            _roofs.TryGetRoofEntity(ground, worldTile, out _))
        {
            return false;
        }

        var surface = surfaceGrid.Value;
        var floor = floorGrid.Value;
        if (IsGround(surface, worldTile) || !IsGround(floor, worldTile))
            return false;

        // whoever and whatever is down there now: they come up when the ground is back
        var inside = new List<EntityUid>();
        var floorMap = Transform(below).MapID;
        foreach (var uid in _lookup.GetEntitiesInRange(new MapCoordinates(worldTile + new Vector2(0.5f), floorMap), 0.5f,
                     LookupFlags.Dynamic))
        {
            if (!Transform(uid).Anchored)
                inside.Add(uid);
        }

        // what was built on the floor of it: steps, wire, a barricade
        var floorIndex = TileOf(floor, worldTile);
        var built = new List<EntityUid>();
        var anchored = _map.GetAnchoredEntitiesEnumerator(floor, floor.Comp, floorIndex);
        while (anchored.MoveNext(out var uid))
        {
            built.Add(uid.Value);
        }

        foreach (var uid in built)
        {
            Del(uid);
        }

        // the partitions of the bunkers next to it, that stood between them and the trench: the earth does now
        foreach (var side in Cardinals)
        {
            if (SeamAt(floor, worldTile + side, -side) is { } seam)
                Del(seam);
        }

        // the ground over it as the ground round it
        _map.SetTile(surface, surface.Comp, TileOf(surface, worldTile), GroundAround(surface, worldTile));

        // down below: a wall if a trench still runs past it, else solid earth; walls that only stood by it go
        if (DugAround(surface, floor, worldTile))
        {
            if (!HasWall(floor, floorIndex))
                Spawn(TrenchWall, _map.GridTileToLocal(floor, floor.Comp, floorIndex));
        }
        else
        {
            _map.SetTile(floor, floor.Comp, floorIndex, Tile.Empty);
        }

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var neighbour = worldTile + new Vector2i(dx, dy);
                if (neighbour == worldTile || !IsGround(surface, neighbour) || DugAround(surface, floor, neighbour))
                    continue;

                var index = TileOf(floor, neighbour);
                RemoveWalls(floor, index);
                if (IsGround(floor, neighbour))
                    _map.SetTile(floor, floor.Comp, index, Tile.Empty);
            }
        }

        foreach (var uid in inside)
        {
            if (!TerminatingOrDeleted(uid))
                _zLevels.TryMove(uid, 1);
        }

        UpdateRims(ground, worldTile);
        return true;
    }

    /// <summary>
    /// A tile of the eight round <paramref name="worldTile"/> is dug out: a trench or a bunker with its floor down below
    /// </summary>
    private bool DugAround(Entity<MapGridComponent> surface, Entity<MapGridComponent> floor, Vector2i worldTile)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var tile = worldTile + new Vector2i(dx, dy);
                if (tile != worldTile && !IsGround(surface, tile) && IsGround(floor, tile))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The tile the ground round <paramref name="worldTile"/> is made of: the first one found next to it
    /// </summary>
    private Tile GroundAround(Entity<MapGridComponent> surface, Vector2i worldTile)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (_map.TryGetTileRef(surface, surface.Comp, TileOf(surface, worldTile + new Vector2i(dx, dy)), out var tileRef) &&
                    !tileRef.Tile.IsEmpty)
                {
                    return tileRef.Tile;
                }
            }
        }

        return new Tile(_tiles[TrenchFloor].TileId);
    }
}
