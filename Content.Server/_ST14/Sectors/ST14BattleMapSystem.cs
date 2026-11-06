using System.Linq;
using System.Numerics;
using Content.Server._ST14.MainHub;
using Content.Server.GameTicking;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// The map players open inside a sector round. It shows the loaded sectors and the passages between them, and the marks
/// the command of the side drew for this round. The marks of a side only ever go to that side
/// </summary>
public sealed partial class ST14BattleMapSystem : EntitySystem
{
    private static readonly ProtoId<NpcFactionPrototype> BugFaction = "Xeno";

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private ST14GalaxyWarSystem _war = default!;
    [Dependency] private ST14SectorSystem _sectors = default!;

    private readonly Dictionary<ST14GalaxyFaction, List<ST14BattleMark>> _marks =
        ST14GalaxyWarRules.Sides.ToDictionary(side => side, _ => new List<ST14BattleMark>());

    private readonly Dictionary<int, byte[]> _terrain = new();

    /// <summary>
    /// Who has the map open or had it open this round, and for which side
    /// </summary>
    private readonly Dictionary<ICommonSession, ST14GalaxyFaction> _viewers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14BattleMapRequestEvent>(OnRequest);
        SubscribeNetworkEvent<ST14BattleMapTerrainRequestEvent>(OnTerrainRequest);
        SubscribeNetworkEvent<ST14BattleMarkAddEvent>(OnMarkAdd);
        SubscribeNetworkEvent<ST14BattleMarkRemoveEvent>(OnMarkRemove);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        InitializeLive();
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        foreach (var marks in _marks.Values)
        {
            marks.Clear();
        }

        _terrain.Clear();
        _viewers.Clear();
        ClearLive();
    }

    private void OnRequest(ST14BattleMapRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var side = ev.Side is { } wanted && _marks.ContainsKey(wanted) && _war.CanCommand(session, wanted)
            ? wanted
            : SideOf(session);

        _viewers[session] = side;
        _open.Add(session);
        _nextLive = TimeSpan.Zero;
        Send(session, side);
    }

    /// <summary>
    /// The side a player fights for this round. The body decides, the preferences only count for those without one:
    /// a player who asked for a bug and got a trooper must not read the plan of the bugs
    /// </summary>
    private ST14GalaxyFaction SideOf(ICommonSession session)
    {
        if (session.AttachedEntity is { } body)
        {
            if (TryComp<NpcFactionMemberComponent>(body, out var member) && _faction.IsMember((body, member), BugFaction))
                return ST14GalaxyFaction.Arachnid;

            if (_mind.TryGetMind(body, out var mindId, out _) &&
                _jobs.MindTryGetJobId(mindId, out var job) &&
                job is { } jobId &&
                _prototype.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs))
            {
                return bugs.Jobs.Contains(jobId) ? ST14GalaxyFaction.Arachnid : ST14GalaxyFaction.Federation;
            }
        }

        return _war.SideOf(session);
    }

    private void Send(ICommonSession session, ST14GalaxyFaction side)
    {
        var commands = ST14GalaxyWarRules.Sides.Where(s => _war.CanCommand(session, s)).ToArray();
        if (_sectors.Planet is not { } planet || !_war.TryGetWar(planet, out var war, out var target))
        {
            RaiseNetworkEvent(new ST14BattleMapStateEvent(false, _ticker.RoundId, side, commands, -1, -1, -1,
                Array.Empty<ST14BattleSector>(), Array.Empty<ST14BattleMark>()), session);
            return;
        }

        var here = -1;
        if (session.AttachedEntity is { } body && CompOrNull<ST14SectorMapComponent>(Transform(body).MapUid) is { } sector)
            here = sector.Cell;

        // the loaded sectors with all they lead to
        var loaded = _sectors.LoadedMaps.Keys.ToHashSet();
        var cells = loaded.SelectMany(cell => war.Grid.Edges[cell].Append(cell)).Distinct();
        var sectors = cells
            .Select(cell => new ST14BattleSector(cell, war.Cells[cell].Code, war.Cells[cell].Faction, loaded.Contains(cell),
                loaded.Contains(cell) ? war.Grid.Edges[cell] : Array.Empty<int>()))
            .ToArray();

        RaiseNetworkEvent(new ST14BattleMapStateEvent(true, _ticker.RoundId, side, commands, here, target, _war.StagingOf(planet),
            sectors, _marks[side].ToArray()), session);
    }

    private void OnTerrainRequest(ST14BattleMapTerrainRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_viewers.ContainsKey(args.SenderSession) || !_sectors.LoadedMaps.TryGetValue(ev.Cell, out var map))
            return;

        if (!_terrain.TryGetValue(ev.Cell, out var blocks))
            _terrain[ev.Cell] = blocks = Rasterize(map);

        RaiseNetworkEvent(new ST14BattleMapTerrainEvent(_ticker.RoundId, ev.Cell, blocks), args.SenderSession);
    }

    /// <summary>
    /// The ground of a sector as the round found it: what is open and what stops a soldier. Nothing that moves is in it
    /// </summary>
    private byte[] Rasterize(EntityUid map)
    {
        const int size = ST14BattleMapTerrainEvent.Blocks;
        const int block = ST14BattleMapTerrainEvent.BlockTiles;

        var blocks = new byte[size * size];
        if (!TryComp<MapComponent>(map, out var mapComp))
            return blocks;

        var physics = GetEntityQuery<PhysicsComponent>();
        foreach (var grid in _map.GetAllGrids(mapComp.MapId))
        {
            var tiles = _map.GetAllTilesEnumerator(grid, grid);
            while (tiles.MoveNext(out var tile))
            {
                var x = tile.Value.GridIndices.X / block;
                var y = tile.Value.GridIndices.Y / block;
                if (tile.Value.GridIndices.X < 0 || tile.Value.GridIndices.Y < 0 || x >= size || y >= size)
                    continue;

                var index = y * size + x;
                if (blocks[index] == ST14BattleMapTerrainEvent.Empty)
                    blocks[index] = ST14BattleMapTerrainEvent.Ground;

                var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid, tile.Value.GridIndices);
                while (anchored.MoveNext(out var uid))
                {
                    if (physics.TryComp(uid, out var body) && body.BodyType == BodyType.Static && body.CanCollide && body.Hard)
                    {
                        blocks[index] = ST14BattleMapTerrainEvent.Obstacle;
                        break;
                    }
                }
            }
        }

        return blocks;
    }

    private void OnMarkAdd(ST14BattleMarkAddEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args, ev.Side) || !Enum.IsDefined(ev.Mark.Kind) || !_sectors.LoadedMaps.ContainsKey(ev.Mark.Cell))
            return;

        var marks = _marks[ev.Side];
        if (marks.Count >= ST14BattleMapStateEvent.MaxMarks)
            return;

        var mark = ev.Mark with { From = ClampToSector(ev.Mark.From), To = ClampToSector(ev.Mark.To) };
        if (mark.IsArrow && (mark.To - mark.From).Length() < 1f)
            return;

        marks.Add(mark);
        SendSide(ev.Side);
    }

    private void OnMarkRemove(ST14BattleMarkRemoveEvent ev, EntitySessionEventArgs args)
    {
        if (!Commands(args, ev.Side))
            return;

        var marks = _marks[ev.Side];
        if (ev.Index < 0)
            marks.Clear();
        else if (ev.Index < marks.Count)
            marks.RemoveAt(ev.Index);
        else
            return;

        SendSide(ev.Side);
    }

    private bool Commands(EntitySessionEventArgs args, ST14GalaxyFaction side)
    {
        if (_marks.ContainsKey(side) && _war.CanCommand(args.SenderSession, side))
            return true;

        Log.Warning($"{args.SenderSession.Name} tried to change the battle plan of the {side} command without being in it");
        return false;
    }

    private void SendSide(ST14GalaxyFaction side)
    {
        foreach (var (session, viewing) in _viewers.ToArray())
        {
            if (session.Status == SessionStatus.Disconnected)
                _viewers.Remove(session);
            else if (viewing == side)
                Send(session, side);
        }
    }

    private static Vector2 ClampToSector(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            return ST14SectorGeometry.Center;

        return Vector2.Clamp(point, Vector2.Zero, new Vector2(ST14SectorGeometry.Size));
    }
}
