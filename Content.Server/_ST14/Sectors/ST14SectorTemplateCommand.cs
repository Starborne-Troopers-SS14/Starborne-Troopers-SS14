using System.Numerics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// Makes a blank sector map for mappers to start from: a square of ground with the sandstorm all around, the landing
/// zone of the troopers on the left, the hive's spawn points and queen places on the right. Saved into the server's
/// data folder
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ST14SectorTemplateCommand : IConsoleCommand
{
    public const int Size = 200;
    public const int StormDepth = 4;
    private const string GridPrototype = "ST14SectorGrid";

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
        // the grid becomes the station of the round: the components come from a prototype, the id is set there
        _entMan.AddComponents(grid, _prototype.Index<EntityPrototype>(GridPrototype), false);
        _entMan.System<MetaDataSystem>().SetEntityName(grid, "Sector");

        var tiles = new List<(Vector2i, Tile)>(Size * Size);
        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                tiles.Add((new Vector2i(x, y), ground));
            }
        }

        mapSystem.SetTiles(grid, tiles);

        // the storm closes the square off on every side
        for (var x = 0; x < Size; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                if (x < StormDepth || y < StormDepth || x >= Size - StormDepth || y >= Size - StormDepth)
                    Place(grid, Storm, x, y);
            }
        }

        // troopers land on the left, in a column a little way off the storm
        const int middle = Size / 2;
        for (var i = -5; i <= 5; i++)
        {
            Place(grid, "ST14SpawnPointEngineer", StormDepth + 6, middle + i * 6);
            Place(grid, "SpawnPointLatejoin", StormDepth + 3, middle + i * 6);
        }

        // the hive comes from the right; the brain bug sits further back, the queens in the depth of the hive's half
        for (var i = -5; i <= 5; i++)
        {
            Place(grid, "ST14SpawnPointBug", Size - StormDepth - 7, middle + i * 6);
        }

        Place(grid, "ST14SpawnPointBrainBug", Size - StormDepth - 12, middle);
        Place(grid, "ST14SectorQueenSlot", Size - StormDepth - 20, middle + 45);
        Place(grid, "ST14SectorQueenSlot", Size - StormDepth - 20, middle - 45);
        Place(grid, "ST14SectorQueenSlot", Size - StormDepth - 35, middle);

        return map;
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
