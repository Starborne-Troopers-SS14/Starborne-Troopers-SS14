using System.Numerics;
using Content.Server.Administration;
using Content.Shared._ST14.Sectors;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Makes a blank sector map for mappers to start from: a hexagon of ground with its points up, the sandstorm along its
/// edges, a passage in the middle of each of the six, the landing zone of the troopers on the left, the hive's spawn
/// points and queen places on the right. Saved into the server's data folder
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ST14SectorTemplateCommand : IConsoleCommand
{
    public const int Size = ST14SectorGeometry.Size;
    public const int StormDepth = 4;

    /// <summary>
    /// From the centre to the middle of an edge: a hexagon with its points up, 192 tiles from point to point
    /// </summary>
    public const float Apothem = ST14SectorGeometry.Apothem;

    private const float GapRadius = 4.5f;

    private static readonly string[] Gates =
    {
        "ST14SectorGateEast", "ST14SectorGateNorthEast", "ST14SectorGateNorthWest",
        "ST14SectorGateWest", "ST14SectorGateSouthWest", "ST14SectorGateSouthEast",
    };

    private const string DefaultTile = "ST14FloorDunes";
    private const string Storm = "ST14SandstormWall";
    private static readonly Color Daylight = Color.FromHex("#E6D6B8");

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private ITileDefinitionManager _tileDef = default!;

    public string Command => "st14_sector_template";
    public string Description => Loc.GetString("st14-sector-template-command-description");
    public string Help => Loc.GetString("st14-sector-template-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var path = new ResPath(args.Length > 0 ? args[0] : "/st14_sector_template.yml").ToRootedPath();
        var tileId = args.Length > 1 ? args[1] : DefaultTile;

        if (!_tileDef.TryGetDefinition(tileId, out var tileDef))
        {
            shell.WriteError(Loc.GetString("st14-sector-template-command-no-tile", ("tile", tileId)));
            return;
        }

        var map = Build(new Tile(tileDef.TileId));
        if (!_entMan.System<MapLoaderSystem>().TrySaveMap(map, path))
        {
            shell.WriteError(Loc.GetString("st14-sector-template-command-failed", ("path", path.ToString())));
            return;
        }

        shell.WriteLine(Loc.GetString("st14-sector-template-command-done", ("path", path.ToString())));
        _entMan.DeleteEntity(map);
    }

    /// <summary>
    /// Builds the blank sector on a new map and returns the map
    /// </summary>
    public EntityUid Build(Tile ground)
    {
        var mapSystem = _entMan.System<SharedMapSystem>();
        var map = mapSystem.CreateMap(out var mapId, runMapInit: false);
        mapSystem.SetAmbientLight(mapId, Daylight);

        var grid = mapSystem.CreateGridEntity(mapId);
        _entMan.System<MetaDataSystem>().SetEntityName(grid, "Sector");

        var tiles = new List<(Vector2i, Tile)>(Size * Size);
        var storm = new List<Vector2i>();
        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                var depth = EdgeDepth(x, y, out var edge);
                if (depth < 0)
                    continue;

                tiles.Add((new Vector2i(x, y), ground));

                // the storm runs along every edge, with a gap for the passage in the middle of it
                if (depth < StormDepth && !InGap(x, y, edge))
                    storm.Add(new Vector2i(x, y));
            }
        }

        mapSystem.SetTiles(grid, tiles);

        foreach (var tile in storm)
        {
            Place(grid, Storm, tile.X, tile.Y);
        }

        for (var edge = 0; edge < Gates.Length; edge++)
        {
            var mid = EdgeMiddle(edge, Apothem - StormDepth / 2f);
            Place(grid, Gates[edge], (int) mid.X, (int) mid.Y);
        }

        // troopers land on the left, in a column a little way off the storm
        const int middle = Size / 2;
        var left = (int) (middle - Apothem) + StormDepth;
        for (var i = -5; i <= 5; i++)
        {
            Place(grid, "ST14SpawnPointEngineer", left + 14, middle + i * 6);
            Place(grid, "SpawnPointLatejoin", left + 11, middle + i * 6);
        }

        // the hive comes from the right; the brain bug sits further back, the queens in the depth of the hive's half
        var right = (int) (middle + Apothem) - StormDepth;
        for (var i = -5; i <= 5; i++)
        {
            Place(grid, "ST14SpawnPointBug", right - 14, middle + i * 6);
        }

        Place(grid, "ST14SpawnPointBrainBug", right - 18, middle);
        Place(grid, "ST14SectorQueenSlot", right - 24, middle + 30);
        Place(grid, "ST14SectorQueenSlot", right - 24, middle - 30);
        Place(grid, "ST14SectorQueenSlot", right - 38, middle);

        return map;
    }

    private static Vector2 EdgeNormal(int edge)
    {
        var angle = MathF.PI / 3 * edge;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static Vector2 EdgeMiddle(int edge, float distance)
    {
        return new Vector2(Size / 2f, Size / 2f) + EdgeNormal(edge) * distance;
    }

    /// <summary>
    /// How deep a tile lies inside the hexagon, negative outside it, and the edge it is closest to
    /// </summary>
    private static float EdgeDepth(int x, int y, out int edge)
    {
        var offset = new Vector2(x + 0.5f - Size / 2f, y + 0.5f - Size / 2f);
        var depth = float.MaxValue;
        edge = 0;
        for (var i = 0; i < Gates.Length; i++)
        {
            var d = Apothem - Vector2.Dot(offset, EdgeNormal(i));
            if (d >= depth)
                continue;

            depth = d;
            edge = i;
        }

        return depth;
    }

    private static bool InGap(int x, int y, int edge)
    {
        var offset = new Vector2(x + 0.5f, y + 0.5f) - EdgeMiddle(edge, Apothem);
        return offset.Length() <= GapRadius;
    }

    /// <summary>
    /// Puts one thing on the template. The hive's spawn points only exist on a server that has the hive, without it
    /// they are left out
    /// </summary>
    private void Place(EntityUid grid, string prototype, int x, int y)
    {
        if (!_prototype.HasIndex<EntityPrototype>(prototype))
            return;

        _entMan.SpawnAtPosition(prototype, new EntityCoordinates(grid, new Vector2(x + 0.5f, y + 0.5f)));
    }
}
