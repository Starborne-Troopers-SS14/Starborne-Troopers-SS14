using System.Linq;
using System.Numerics;
using Content.Server._ST14.MainHub;
using Content.Server._ST14.ZLevels.Fortifications;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Walking from one sector into the next. Every stretch of every edge of a sector leads into the sector behind it, on
/// every z-level: a mob that walks into the storm along an edge comes out at the same place of the shared edge in the
/// neighbouring sector, on the same level, a step clear of the storm there. The neighbour has to be loaded this round,
/// and the hive only goes as far as the front: the target, the sector the attacker gathers in, and the sectors of the
/// Federation from which a sector of the hive can be attacked.
/// Under the ground the other side must be dug out too: a trooper with an entrenching tool in hand starts digging it,
/// otherwise the way is closed
/// </summary>
public sealed partial class ST14SectorCrossingSystem : EntitySystem
{
    /// <summary>
    /// How far into the storm from its inner edge a mob has to wade on the surface to cross. The storm is
    /// <see cref="ST14SectorTemplateCommand.StormDepth"/> tiles deep, only its outer tile can be waded into
    /// </summary>
    private const float SurfaceReach = ST14SectorTemplateCommand.StormDepth - 0.4f;

    /// <summary>
    /// Below the ground there is no storm: a trench or a tunnel dug up to the line where it starts on the surface crosses
    /// </summary>
    private const float UndergroundReach = ST14SectorTemplateCommand.StormDepth + 0.9f;

    /// <summary>
    /// How much further in than the line it crosses at a mob comes out, so it does not cross straight back
    /// </summary>
    private const float ArrivalMargin = 0.6f;

    private const float MinOutwardSpeed = 0.05f;
    private const int FreeSearch = 3;

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.2);
    private static readonly TimeSpan CrossCooldown = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan PopupDelay = TimeSpan.FromSeconds(3);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;
    [Dependency] private ST14SharedZLevelsSystem _zLevels = default!;
    [Dependency] private ST14TrenchSystem _trenches = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _crossed = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastPopup = new();
    private readonly List<(EntityUid Mob, int Cell, int Depth, EntityUid Level)> _candidates = new();
    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_sectors.Planet is not { } planet || _timing.CurTime < _nextCheck || !_war.TryGetWar(planet, out var war, out _))
            return;

        _nextCheck = _timing.CurTime + CheckEvery;
        _candidates.Clear();

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } level || _mobState.IsDead(uid))
                continue;

            var cell = _sectors.CellOf(level);
            if (cell < 0 || _sectors.LevelsOfCell(cell) is not { } levels)
                continue;

            foreach (var (depth, map) in levels)
            {
                if (map != level)
                    continue;

                _candidates.Add((uid, cell, depth, level));
                break;
            }
        }

        foreach (var (mob, cell, depth, level) in _candidates)
        {
            TryCross(war, mob, cell, depth, level);
        }
    }

    private void TryCross(ST14PlanetWar war, EntityUid mob, int cell, int depth, EntityUid level)
    {
        if (_crossed.TryGetValue(mob, out var last) && _timing.CurTime - last < CrossCooldown)
            return;

        if (!TryGetSectorGrid(cell, out var here))
            return;

        var world = _transform.GetWorldPosition(mob);
        var local = Vector2.Transform(world, _transform.GetInvWorldMatrix(here.Owner));
        var offset = local - ST14SectorGeometry.Center;
        var reach = depth < 0 ? UndergroundReach : SurfaceReach;

        // the edge the mob is wading into, and how far out it is
        var edge = -1;
        var best = float.MinValue;
        for (var i = 0; i < 6; i++)
        {
            var outward = Vector2.Dot(offset, ST14SectorGeometry.EdgeNormal(i));
            if (outward <= best)
                continue;

            best = outward;
            edge = i;
        }

        if (edge < 0 || ST14SectorGeometry.Apothem - best > reach)
            return;

        // only a mob heading out crosses, not one walking along the edge or back in. Pressed against the storm it does
        // not move, so what it wants counts first, then where it goes
        var velocity = CompOrNull<Content.Shared.Movement.Components.InputMoverComponent>(mob) is { WishDir: var wish } && wish.LengthSquared() > 0.0001f
            ? wish
            : CompOrNull<PhysicsComponent>(mob)?.LinearVelocity ?? Vector2.Zero;
        if (Vector2.Dot(velocity, ST14SectorGeometry.EdgeNormal(edge)) < MinOutwardSpeed)
            return;

        if (Leads(war, cell, edge, IsHive(mob), out var refusal) is not { } next || refusal != null ||
            _sectors.LevelsOfCell(next) is not { } nextLevels || !nextLevels.TryGetValue(depth, out var nextLevel) ||
            !TryGetSectorGrid(next, out var there))
        {
            Tell(mob, refusal ?? "st14-sector-gate-closed");
            return;
        }

        // the same place on the shared edge, seen from the other sector: along the edge the other way round
        var back = Array.IndexOf(war.Grid.Edges[next], cell);
        if (back < 0)
            back = (edge + 3) % 6;

        var along = Vector2.Dot(offset, Tangent(edge));
        var arrive = ST14SectorGeometry.Center
                     + ST14SectorGeometry.EdgeNormal(back) * (ST14SectorGeometry.Apothem - reach - ArrivalMargin)
                     - Tangent(back) * along;
        var arriveWorld = Vector2.Transform(arrive, _transform.GetWorldMatrix(there.Owner));

        if (FreeSpot(nextLevel, depth, arriveWorld, ST14SectorGeometry.EdgeNormal(back)) is not { } spot)
        {
            // under the ground the other side has to be dug out first
            if (depth == -1 && TryDigThrough(mob, nextLevel, arriveWorld))
            {
                _crossed[mob] = _timing.CurTime;
                Tell(mob, "st14-sector-gate-digging");
                return;
            }

            Tell(mob, depth < 0 ? "st14-sector-gate-solid" : "st14-sector-gate-closed");
            return;
        }

        var mapId = Transform(nextLevel).MapID;
        var pulled = CompOrNull<PullerComponent>(mob)?.Pulling;
        _transform.SetMapCoordinates(mob, new MapCoordinates(spot, mapId));
        if (pulled is { } other && !TerminatingOrDeleted(other))
            _transform.SetMapCoordinates(other, new MapCoordinates(spot, mapId));

        _crossed[mob] = _timing.CurTime;
        if (pulled is { } dragged)
            _crossed[dragged] = _timing.CurTime;
    }

    /// <summary>
    /// The sector an edge of a loaded sector leads into, null with the reason (a locale id) when it leads nowhere: no
    /// sector behind it, a sector not loaded this round, or, for the hive, one deep in the rear of the Federation
    /// </summary>
    public int? Leads(ST14PlanetWar war, int cell, int edge, bool hive, out string? refusal)
    {
        refusal = "st14-sector-gate-closed";
        if (cell < 0 || cell >= war.Cells.Length || edge < 0 || edge >= war.Grid.Edges[cell].Length)
            return null;

        var next = war.Grid.Edges[cell][edge];
        if (_sectors.LevelsOfCell(next) == null)
            return null;

        if (hive && !HiveMayEnter(war, next))
        {
            refusal = "st14-sector-gate-rear";
            return null;
        }

        refusal = null;
        return next;
    }

    /// <summary>
    /// The direction along an edge, counter-clockwise round the sector
    /// </summary>
    private static Vector2 Tangent(int edge)
    {
        var normal = ST14SectorGeometry.EdgeNormal(edge);
        return new Vector2(-normal.Y, normal.X);
    }

    /// <summary>
    /// The hive goes only as far as the front: the sectors of the battle, the sectors it holds, and the sectors of the
    /// Federation next to a sector of the hive - from those a sector of the hive can be attacked
    /// </summary>
    private bool HiveMayEnter(ST14PlanetWar war, int cell)
    {
        if (cell == _sectors.Target || cell == _sectors.Staging || war.Cells[cell].Faction == ST14GalaxyFaction.Arachnid)
            return true;

        foreach (var neighbour in war.Grid.Neighbours[cell])
        {
            if (war.Cells[neighbour].Faction == ST14GalaxyFaction.Arachnid)
                return true;
        }

        return false;
    }

    private bool IsHive(EntityUid mob)
    {
        return TryComp<NpcFactionMemberComponent>(mob, out var member) &&
               _faction.IsMember((mob, member), _sectors.HiveFaction);
    }

    /// <summary>
    /// The ground of a sector (its level 0 grid), which the sector tiles are counted on
    /// </summary>
    private bool TryGetSectorGrid(int cell, out Entity<MapGridComponent> grid)
    {
        grid = default;
        if (_sectors.LevelsOfCell(cell) is not { } levels || !levels.TryGetValue(0, out var main) ||
            !_zLevels.TryGetLayerGrid(main, out var found))
        {
            return false;
        }

        grid = found.Value;
        return true;
    }

    /// <summary>
    /// The nearest spot to <paramref name="world"/> on the level a mob can stand on: floor, nothing solid. Searched
    /// along the edge and a little further in, never out towards the storm
    /// </summary>
    private Vector2? FreeSpot(EntityUid level, int depth, Vector2 world, Vector2 outward)
    {
        if (!_zLevels.TryGetLayerGrid(level, out var grid))
            return depth > 0 ? world : null;

        if (Free(grid.Value, world, depth))
            return world;

        var along = new Vector2(-outward.Y, outward.X);
        for (var step = 1; step <= FreeSearch; step++)
        {
            foreach (var candidate in new[]
                     {
                         world + along * step, world - along * step, world - outward * step,
                         world - outward * step + along * step, world - outward * step - along * step,
                     })
            {
                if (Free(grid.Value, candidate, depth))
                    return candidate;
            }
        }

        return null;
    }

    private bool Free(Entity<MapGridComponent> grid, Vector2 world, int depth)
    {
        var tile = _map.WorldToTile(grid, grid.Comp, world);
        if (!_map.TryGetTileRef(grid, grid.Comp, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return depth > 0; // up in the air a mob may come out over nothing, it falls as it would anywhere

        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (!TryComp<FixturesComponent>(uid.Value, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A trooper in a trench with an entrenching tool in hand digs on into the trench layer of the next sector
    /// </summary>
    private bool TryDigThrough(EntityUid mob, EntityUid level, Vector2 world)
    {
        if (_hands.GetActiveItem(mob) is not { } held || !TryComp<ST14TrenchDiggerComponent>(held, out var digger) ||
            !_zLevels.TryMapUp(level, out var ground))
        {
            return false;
        }

        // already digging: the swing goes on by itself
        if (TryComp<Content.Shared.DoAfter.DoAfterComponent>(mob, out var doAfters) && doAfters.DoAfters.Values.Any(d => !d.Completed && !d.Cancelled))
            return true;

        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        return _trenches.TryDigAcross((held, digger), mob, ground.Owner, tile);
    }

    private void Tell(EntityUid mob, string message)
    {
        var now = _timing.CurTime;
        if (_lastPopup.TryGetValue(mob, out var last) && now - last < PopupDelay)
            return;

        _lastPopup[mob] = now;
        _popup.PopupEntity(Loc.GetString(message), mob, mob, PopupType.MediumCaution);
    }
}
