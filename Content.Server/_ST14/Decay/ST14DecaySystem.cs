using System.Linq;
using System.Numerics;
using Content.Server._ST14.Sectors;
using Content.Server._ST14.ZLevels.Fortifications;
using Content.Server.Destructible;
using Content.Server.Stack;
using Content.Shared._ST14.Construction.Components;
using Content.Shared._ST14.Decay;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Decay;

/// <summary>
/// Rot: what the troopers build falls apart by itself unless it is kept up, so the abandoned and the useless leave the
/// war without anybody clearing them. A building is kept by a supply point in reach - a bunker base or a maintenance
/// tunnel - that spends maintenance supplies on it. A building without one rots: in <see cref="Lifetime"/> it is gone,
/// a trench caves in. The clock only runs while the sector is loaded, during a battle
/// </summary>
public sealed partial class ST14DecaySystem : EntitySystem
{
    /// <summary>
    /// How long a building lasts without maintenance
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    /// <summary>
    /// Maintenance supplies a building takes in an hour, and a tile of trench: one supply keeps twenty buildings or a
    /// hundred tiles of trench for an hour
    /// </summary>
    public const float BuildingUpkeep = 0.05f;
    public const float TrenchUpkeep = 0.01f;

    public static readonly ProtoId<StackPrototype> MaintenanceSupplies = "ST14MaintenanceSupplies";
    private static readonly ProtoId<DamageTypePrototype> RotDamage = "Structural";
    private static readonly ProtoId<DamageTypePrototype> RotDamageFallback = "Blunt";

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;
    [Dependency] private ST14SharedZLevelsSystem _zLevels = default!;
    [Dependency] private ST14TrenchSystem _trenches = default!;

    private TimeSpan _nextCheck;
    private readonly List<(EntityUid Uid, ST14MaintenanceProviderComponent Comp, int Cell, Vector2 Position)> _providers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _providers.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_sectors.Planet == null || _timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckEvery;
        var hours = (float) CheckEvery.TotalHours;

        FindProviders();
        RotBuildings(hours);
        RotTrenches(hours);
    }

    private void FindProviders()
    {
        _providers.Clear();
        var query = EntityQueryEnumerator<ST14MaintenanceProviderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapUid is not { } level || _sectors.CellOf(level) is var cell && cell < 0)
                continue;

            _providers.Add((uid, comp, cell, _transform.GetWorldPosition(xform)));
        }
    }

    #region Buildings

    private void RotBuildings(float hours)
    {
        var buildings = new List<EntityUid>();
        var built = EntityQueryEnumerator<ST14EngineerStructureComponent, TransformComponent>();
        while (built.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is { } level && _sectors.CellOf(level) >= 0)
                buildings.Add(uid);
        }

        var upgrades = EntityQueryEnumerator<ST14BunkerUpgradeComponent, TransformComponent>();
        while (upgrades.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is { } level && _sectors.CellOf(level) >= 0)
                buildings.Add(uid);
        }

        foreach (var uid in buildings)
        {
            var xform = Transform(uid);
            var cell = _sectors.CellOf(xform.MapUid!.Value);
            var supplied = Keep(cell, _transform.GetWorldPosition(xform), BuildingUpkeep * hours);

            if (supplied)
            {
                if (TryComp<ST14DecayComponent>(uid, out var kept))
                    kept.Supplied = true;
                continue;
            }

            var decay = EnsureComp<ST14DecayComponent>(uid);
            decay.Supplied = false;
            decay.Rot += CheckEvery;
            Rot(uid, decay);
        }
    }

    /// <summary>
    /// A building that went another while without maintenance: it takes its share of damage, or, with nothing to
    /// damage, goes once its time is up
    /// </summary>
    private void Rot(EntityUid uid, ST14DecayComponent decay)
    {
        if (TryComp<DamageableComponent>(uid, out var damageable) && HasComp<Content.Server.Destructible.DestructibleComponent>(uid))
        {
            var total = _destructible.DestroyedAt(uid);
            if (total > FixedPoint2.Zero && total < FixedPoint2.MaxValue)
            {
                var type = _damageable.GetAllDamage((uid, damageable)).DamageDict.ContainsKey(RotDamage) ? RotDamage : RotDamageFallback;
                var share = FixedPoint2.New(total.Float() * (float) (CheckEvery / Lifetime));
                _damageable.TryChangeDamage((uid, damageable), new DamageSpecifier(_prototype.Index(type), share),
                    ignoreResistances: true, interruptsDoAfters: false);
                return;
            }
        }

        if (decay.Rot >= Lifetime)
            QueueDel(uid);
    }

    #endregion

    #region Trenches

    private void RotTrenches(float hours)
    {
        foreach (var (cell, _) in _sectors.LoadedMaps)
        {
            if (_sectors.LevelsOfCell(cell) is not { } levels || !levels.TryGetValue(0, out var ground) ||
                !levels.TryGetValue(-1, out var below) ||
                !_zLevels.TryGetLayerGrid(ground, out var surface) || !_zLevels.TryGetLayerGrid(below, out var floor))
            {
                continue;
            }

            var rot = EnsureComp<ST14TrenchRotComponent>(ground);
            var dug = new HashSet<Vector2i>();
            var tiles = _map.GetAllTilesEnumerator(floor.Value, floor.Value.Comp);
            while (tiles.MoveNext(out var tileRef))
            {
                var world = Vector2.Transform(tileRef.Value.GridIndices + new Vector2(0.5f), _transform.GetWorldMatrix(floor.Value.Owner));
                var worldTile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));

                // a trench: floor down below, the surface over it dug open
                var over = _map.WorldToTile(surface.Value, surface.Value.Comp, world);
                if (_map.TryGetTileRef(surface.Value, surface.Value.Comp, over, out var surfaceTile) && !surfaceTile.Tile.IsEmpty)
                    continue;

                dug.Add(worldTile);
                if (Keep(cell, world, TrenchUpkeep * hours))
                    continue;

                var seconds = rot.Rot.GetValueOrDefault(worldTile) + (float) CheckEvery.TotalSeconds;
                rot.Rot[worldTile] = seconds;
            }

            foreach (var tile in rot.Rot.Keys.ToList())
            {
                if (!dug.Contains(tile))
                {
                    rot.Rot.Remove(tile);
                    continue;
                }

                // rotted through: it caves in. A bunker does not, its roof holds (FillTrench leaves it)
                if (rot.Rot[tile] >= Lifetime.TotalSeconds && _trenches.FillTrench(ground, tile))
                    rot.Rot.Remove(tile);
            }
        }
    }

    #endregion

    #region Supplies

    /// <summary>
    /// Pays the upkeep of something standing at <paramref name="world"/> in a sector from the nearest supply point in
    /// reach that has supplies. False when none does: then it rots
    /// </summary>
    private bool Keep(int cell, Vector2 world, float upkeep)
    {
        foreach (var (uid, comp, _, _) in _providers
                     .Where(p => p.Cell == cell && (p.Position - world).LengthSquared() <= p.Comp.Radius * p.Comp.Radius)
                     .OrderBy(p => (p.Position - world).LengthSquared()))
        {
            if (comp.Credit < upkeep && !OpenSupply(uid, comp))
                continue;

            comp.Credit -= upkeep;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Opens one more maintenance supply from the storage of the point
    /// </summary>
    private bool OpenSupply(EntityUid uid, ST14MaintenanceProviderComponent comp)
    {
        if (!_container.TryGetContainer(uid, comp.Container, out var storage))
            return false;

        foreach (var item in storage.ContainedEntities)
        {
            if (!TryComp<StackComponent>(item, out var stack) || stack.StackTypeId != MaintenanceSupplies || stack.Count <= 0)
                continue;

            if (!_stack.TryUse((item, stack), 1))
                continue;

            comp.Credit += 1f;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Maintenance supplies in the storage of a point, the one being spent counted as what is left of it
    /// </summary>
    public float SuppliesLeft(EntityUid uid, ST14MaintenanceProviderComponent comp)
    {
        var count = comp.Credit;
        if (!_container.TryGetContainer(uid, comp.Container, out var storage))
            return count;

        foreach (var item in storage.ContainedEntities)
        {
            if (TryComp<StackComponent>(item, out var stack) && stack.StackTypeId == MaintenanceSupplies)
                count += stack.Count;
        }

        return count;
    }

    #endregion
}
