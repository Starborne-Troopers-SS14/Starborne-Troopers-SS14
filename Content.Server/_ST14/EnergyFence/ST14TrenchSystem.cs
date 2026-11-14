using Content.Shared._ST14.EnergyFence;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// Cable trenches. The planet ground counts as a covered floor, so cables under it are hidden (only a T-ray shows them)
/// and cannot be laid there. A trooper digs a small trench with an entrenching tool, lays the cable in it
/// and fills it back in. Digging the trench again opens the cable up for cutting
/// </summary>
public sealed partial class ST14TrenchSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TileSystem _tile = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14TrenchToolComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ST14TrenchToolComponent, ST14TrenchDoAfterEvent>(OnDoAfter);
    }

    private void OnAfterInteract(Entity<ST14TrenchToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null && !HasComp<MapGridComponent>(args.Target))
            return;

        if (!TryGetTile(args.ClickLocation, out var grid, out var tileRef))
            return;

        if (!TryGetChange(ent.Comp, GetTileId(tileRef), out var newTile, out var dig))
            return;

        args.Handled = true;

        var location = _map.GridTileToLocal(grid, grid, tileRef.GridIndices);
        var doAfter = new DoAfterArgs(EntityManager,
            args.User,
            dig ? ent.Comp.DigTime : ent.Comp.FillTime,
            new ST14TrenchDoAfterEvent(GetNetCoordinates(location), newTile),
            ent,
            used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _audio.PlayPvs(ent.Comp.DigSound, location);
        _popup.PopupEntity(Loc.GetString(dig ? "st14-trench-dig-start" : "st14-trench-fill-start"), args.User, args.User);
    }

    private void OnDoAfter(Entity<ST14TrenchToolComponent> ent, ref ST14TrenchDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        var location = GetCoordinates(args.Location);
        if (!TryGetTile(location, out var grid, out var tileRef))
            return;

        // The ground may have changed while digging
        if (!TryGetChange(ent.Comp, GetTileId(tileRef), out var newTile, out _)
            || newTile != args.NewTile
            || !_proto.TryIndex<ContentTileDefinition>(newTile, out var def))
        {
            return;
        }

        _tile.ReplaceTile(tileRef, def, grid, grid);
        _audio.PlayPvs(ent.Comp.DigSound, location);
    }

    private string GetTileId(TileRef tileRef)
    {
        return _tileDefs[tileRef.Tile.TypeId].ID;
    }

    /// <summary>
    /// Ground → trench, or trench → ground
    /// </summary>
    private static bool TryGetChange(ST14TrenchToolComponent tool, string current, out string newTile, out bool dig)
    {
        foreach (var (ground, trench) in tool.Trenches)
        {
            if (ground.Id == current)
            {
                newTile = trench.Id;
                dig = true;
                return true;
            }

            if (trench.Id == current)
            {
                newTile = ground.Id;
                dig = false;
                return true;
            }
        }

        newTile = string.Empty;
        dig = false;
        return false;
    }

    private bool TryGetTile(EntityCoordinates coords, out Entity<MapGridComponent> grid, out TileRef tileRef)
    {
        grid = default;
        tileRef = default;

        if (_transform.GetGrid(coords) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
            return false;

        grid = (gridUid, gridComp);
        tileRef = _map.GetTileRef(grid, coords);
        return !tileRef.Tile.IsEmpty;
    }
}
