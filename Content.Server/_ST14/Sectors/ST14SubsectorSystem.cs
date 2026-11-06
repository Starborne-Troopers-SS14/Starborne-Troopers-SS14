using System.Linq;
using System.Numerics;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared._ST14.ST14Resources.Components;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// A sector is split into subsectors, one around each of its resource points. A point is held by the side whose
/// building stands on it: a drill of the Federation, a nest of the hive. Whoever holds every point of a sector when
/// the round ends takes the sector
/// </summary>
public sealed partial class ST14SubsectorSystem : EntitySystem
{
    /// <summary>
    /// How far from a resource point a building still holds it
    /// </summary>
    private const float HoldRange = 1.25f;

    /// <summary>
    /// Tiles from the middle of the sector to the points put on a map made without any
    /// </summary>
    private const float FallbackDistance = 42f;

    private const int FreeTileSearch = 12;

    private static readonly EntProtoId[] FallbackFields = { "ST14ArmiumField", "ST14AuronField" };
    private static readonly EntProtoId Nest = "ST14HiveNest";

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<ST14ResourcePointHolderComponent>> _holders = new();
    private readonly HashSet<Entity<ST14ResourceFieldComponent>> _fields = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14NestPlanterComponent, MapInitEvent>(OnPlanterInit);
        SubscribeLocalEvent<ST14NestPlanterComponent, ComponentShutdown>(OnPlanterShutdown);
        SubscribeLocalEvent<ST14NestPlanterComponent, ST14PlantNestActionEvent>(OnPlant);
        SubscribeLocalEvent<ST14NestPlanterComponent, ST14PlantNestDoAfterEvent>(OnPlanted);
    }

    #region Points

    /// <summary>
    /// The side holding the point at these coordinates: the one side with a building on it, null for none or both
    /// </summary>
    public ST14GalaxyFaction? HolderAt(EntityCoordinates coordinates)
    {
        _holders.Clear();
        _lookup.GetEntitiesInRange(coordinates, HoldRange, _holders);

        ST14GalaxyFaction? side = null;
        foreach (var holder in _holders)
        {
            if (side != null && side != holder.Comp.Side)
                return null;

            side = holder.Comp.Side;
        }

        return side;
    }

    /// <summary>
    /// All resource points on the loaded sectors, in a fixed order: by sector, then from the west
    /// </summary>
    public List<ST14SubsectorPoint> Points(IReadOnlyDictionary<int, EntityUid> maps)
    {
        var cells = maps.ToDictionary(p => p.Value, p => p.Key);
        var points = new List<ST14SubsectorPoint>();
        var query = EntityQueryEnumerator<ST14ResourceFieldComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid is not { } map || !cells.TryGetValue(map, out var cell) || xform.GridUid is not { } grid)
                continue;

            var local = Vector2.Transform(_transform.GetWorldPosition(xform), _transform.GetInvWorldMatrix(grid));
            points.Add(new ST14SubsectorPoint(cell, local, HolderAt(xform.Coordinates)));
        }

        return points.OrderBy(p => p.Cell).ThenBy(p => p.Position.X).ThenBy(p => p.Position.Y).ToList();
    }

    /// <summary>
    /// Every loaded sector with resource points and the side holding all of them, null when no side does
    /// </summary>
    public Dictionary<int, ST14GalaxyFaction?> Holders(IReadOnlyDictionary<int, EntityUid> maps)
    {
        var holders = new Dictionary<int, ST14GalaxyFaction?>();
        foreach (var group in Points(maps).GroupBy(p => p.Cell))
        {
            var first = group.First().Holder;
            holders[group.Key] = first != null && group.All(p => p.Holder == first) ? first : null;
        }

        return holders;
    }

    /// <summary>
    /// A sector map made without resource points gets two, across the middle. A sector the hive holds gets a nest on
    /// every free point when its map is fresh, so the Federation has something to break
    /// </summary>
    public void PreparePoints(EntityUid map, HashSet<Entity<MapGridComponent>> grids, int cell, bool hiveHeld, bool fresh)
    {
        if (grids.Count == 0)
            return;

        var grid = grids.MaxBy(g => g.Comp.LocalAABB.Size.LengthSquared());
        var fields = new List<EntityUid>();
        var query = EntityQueryEnumerator<ST14ResourceFieldComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == map)
                fields.Add(uid);
        }

        var made = false;
        if (fields.Count == 0)
        {
            // turned by the number of the sector, so neighbouring sectors do not all look alike
            var angle = cell * 2.39996f;
            var across = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * FallbackDistance;
            for (var i = 0; i < FallbackFields.Length; i++)
            {
                var at = ST14SectorGeometry.Center + (i == 0 ? across : -across);
                if (FreeTile(grid, at) is not { } tile)
                    continue;

                fields.Add(Spawn(FallbackFields[i], _map.GridTileToLocal(grid, grid.Comp, tile)));
            }

            made = fields.Count > 0;
        }

        if (!hiveHeld || !fresh && !made)
            return;

        foreach (var field in fields)
        {
            var coordinates = Transform(field).Coordinates;
            if (HolderAt(coordinates) == null)
                Spawn(Nest, coordinates);
        }
    }

    /// <summary>
    /// The nearest tile to the point that has floor and nothing solid on it
    /// </summary>
    private Vector2i? FreeTile(Entity<MapGridComponent> grid, Vector2 local)
    {
        var start = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));
        for (var radius = 0; radius <= FreeTileSearch; radius++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                        continue;

                    var tile = start + new Vector2i(dx, dy);
                    if (!ST14SectorGeometry.Contains((Vector2) tile + new Vector2(0.5f)) ||
                        !_map.TryGetTileRef(grid, grid.Comp, tile, out var tileRef) || tileRef.Tile.IsEmpty || Blocked(grid, tile))
                    {
                        continue;
                    }

                    return tile;
                }
            }
        }

        return null;
    }

    private bool Blocked(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<PhysicsComponent>(uid, out var physics) && physics is { CanCollide: true, Hard: true, BodyType: BodyType.Static })
                return true;
        }

        return false;
    }

    #endregion

    #region Nests

    private void OnPlanterInit(Entity<ST14NestPlanterComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnPlanterShutdown(Entity<ST14NestPlanterComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    /// <summary>
    /// The resource point the bug stands next to, and why a nest cannot grow there when it cannot
    /// </summary>
    private EntityUid? PointFor(Entity<ST14NestPlanterComponent> ent, out string? refusal)
    {
        _fields.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _fields);
        if (_fields.Count == 0)
        {
            refusal = "st14-nest-no-point";
            return null;
        }

        var position = _transform.GetWorldPosition(ent);
        var field = _fields.MinBy(f => (_transform.GetWorldPosition(f) - position).LengthSquared()).Owner;

        _holders.Clear();
        _lookup.GetEntitiesInRange(Transform(field).Coordinates, HoldRange, _holders);
        refusal = _holders.Any(h => h.Comp.Side == ST14GalaxyFaction.Federation) ? "st14-nest-enemy"
            : _holders.Count > 0 ? "st14-nest-already"
            : null;

        return field;
    }

    private void OnPlant(Entity<ST14NestPlanterComponent> ent, ref ST14PlantNestActionEvent args)
    {
        if (args.Handled)
            return;

        if (PointFor(ent, out var refusal) is not { } field || refusal != null)
        {
            _popup.PopupEntity(Loc.GetString(refusal ?? "st14-nest-no-point"), ent, ent, PopupType.SmallCaution);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.Delay, new ST14PlantNestDoAfterEvent(), ent, target: field)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("st14-nest-start"), ent, PopupType.Medium);
    }

    private void OnPlanted(Entity<ST14NestPlanterComponent> ent, ref ST14PlantNestDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } field || TerminatingOrDeleted(field))
            return;

        args.Handled = true;
        if (PointFor(ent, out var refusal) != field || refusal != null)
        {
            _popup.PopupEntity(Loc.GetString(refusal ?? "st14-nest-no-point"), ent, ent, PopupType.SmallCaution);
            return;
        }

        Spawn(ent.Comp.Nest, Transform(field).Coordinates);
        _popup.PopupEntity(Loc.GetString("st14-nest-done"), ent, PopupType.Medium);
    }

    #endregion
}
