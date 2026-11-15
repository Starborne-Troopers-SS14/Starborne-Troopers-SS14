#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.Weather;
using Content.Server._ST14.ZLevels.Layers;
using Content.Shared._ST14.Weather;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._ST14;

/// <summary>
/// The five z-levels of a planet sector: building them around a sector-like map, saving and loading the whole
/// network, and one weather on all of them.
/// </summary>
[TestOf(typeof(ST14ZLayerSystem))]
public sealed class ST14ZLayersTest : GameTest
{
    private const int Size = 12;

    /// <summary>
    /// A sector-like main map: a separate grid of floor on a map, with a sandstorm ring on its border
    /// </summary>
    private EntityUid MakeSector(out int ringTiles)
    {
        var map = SEntMan.System<SharedMapSystem>();
        var floor = Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;

        var mapUid = map.CreateMap(out var mapId);
        var grid = map.CreateGridEntity(mapId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < Size; x++)
        for (var y = 0; y < Size; y++)
            tiles.Add((new Vector2i(x, y), new Tile(floor)));
        map.SetTiles(grid, tiles);

        // the outer ring is the sandstorm, as on a real sector
        ringTiles = 0;
        for (var x = 0; x < Size; x++)
        for (var y = 0; y < Size; y++)
        {
            if (x != 0 && y != 0 && x != Size - 1 && y != Size - 1)
                continue;

            SEntMan.SpawnEntity("ST14SandstormWall", new EntityCoordinates(grid, x + 0.5f, y + 0.5f));
            // corners touch open ground only diagonally, so they are not part of the inner ring
            if (!(x is 0 or Size - 1 && y is 0 or Size - 1))
                ringTiles++;
        }

        return mapUid;
    }

    [Test]
    public async Task BuildFiveLevelsTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var maps = SEntMan.System<SharedMapSystem>();
            var tileMan = Server.ResolveDependency<ITileDefinitionManager>();

            var main = MakeSector(out var ring);
            var levels = layers.EnsureLayers(main, "ST14PlanetSector");

            Assert.That(levels.Keys.OrderBy(d => d), Is.EqualTo(new[] { -2, -1, 0, 1, 2 }));
            Assert.That(levels[0], Is.EqualTo(main));

            // depths and links
            foreach (var (depth, level) in levels)
            {
                var zMap = SComp<ST14ZMapComponent>(level);
                Assert.That(zMap.Depth, Is.EqualTo(depth));
                Assert.That(zMap.MapAbove, Is.EqualTo(levels.TryGetValue(depth + 1, out var up) ? up : null));
                Assert.That(zMap.MapBelow, Is.EqualTo(levels.TryGetValue(depth - 1, out var down) ? down : null));
            }

            // underground: tiles under the open ground and the ring, nothing beyond; an edge entity on each ring tile
            foreach (var (depth, fill) in new[] { (-1, "FloorDirt"), (-2, "FloorCave") })
            {
                var level = levels[depth];
                var grid = SComp<MapGridComponent>(level);
                Assert.That(maps.GetTileRef(level, grid, new Vector2i(5, 5)).Tile.TypeId, Is.EqualTo(tileMan[fill].TileId));
                Assert.That(maps.GetTileRef(level, grid, new Vector2i(0, 5)).Tile.TypeId, Is.EqualTo(tileMan[fill].TileId), "ring tile");
                Assert.That(maps.GetTileRef(level, grid, new Vector2i(0, 0)).Tile.IsEmpty, "corner of the storm is not ground");
                Assert.That(maps.GetTileRef(level, grid, new Vector2i(-3, 5)).Tile.IsEmpty, "outside the sector");

                var edges = 0;
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (xform.MapUid == level && meta.EntityPrototype?.ID == "ST14ZLayerEdge")
                        edges++;
                }

                Assert.That(edges, Is.EqualTo(ring), $"edge rock on level {depth}");
            }

            // sky levels: open air
            foreach (var depth in new[] { 1, 2 })
            {
                Assert.That(SComp<MapGridComponent>(levels[depth]).ChunkCount, Is.EqualTo(0));
            }

            // roofs: the ground of level 0 covers the trenches, nothing covers level 0 yet
            var roofs = SEntMan.System<SharedRoofSystem>();
            var trenches = levels[-1];
            Assert.That(roofs.IsRooved((trenches, SComp<MapGridComponent>(trenches), SComp<RoofComponent>(trenches)), new Vector2i(5, 5)));
        });
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ST14ZLayersDummy
  components:
  - type: ST14ZPhysics
  - type: Physics
    bodyType: Dynamic
";

    /// <summary>
    /// Digging through the floor of the sector under a trooper drops them into a trench on -1, where they stand
    /// </summary>
    [Test]
    public async Task DigIntoTrenchTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid trooper = default;
        EntityUid grid = default;

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            grid = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>().TryGetLayerGrid(levels[0], out var g) ? g!.Value.Owner : default;
            trooper = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(grid, 5.5f, 5.5f));
        });

        await Server.WaitPost(() =>
            SEntMan.System<SharedTransformSystem>().SetWorldPosition(trooper, new Vector2(5.4f, 5.5f)));
        await RunTicksSync(30);

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(trooper).MapUid, Is.EqualTo(levels[0]), "fell before digging");
            var maps = SEntMan.System<SharedMapSystem>();
            maps.SetTile(grid, SComp<MapGridComponent>(grid), new Vector2i(5, 5), Tile.Empty);
        });
        await RunTicksSync(120);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(trooper).MapUid, Is.EqualTo(levels[-1]), "did not drop into the trench");
            Assert.That(SComp<ST14ZPhysicsComponent>(trooper).CurrentZLevel, Is.EqualTo(-1));
            Assert.That(SComp<ST14ZPhysicsComponent>(trooper).LocalPosition, Is.EqualTo(0f).Within(0.05), "not standing on the trench floor");

            // the dug-out tile no longer roofs the trench under it
            var roofs = SEntMan.System<SharedRoofSystem>();
            var trench = levels[-1];
            Assert.That(roofs.IsRooved((trench, SComp<MapGridComponent>(trench), SComp<RoofComponent>(trench)), new Vector2i(5, 5)), Is.False);
            Assert.That(roofs.IsRooved((trench, SComp<MapGridComponent>(trench), SComp<RoofComponent>(trench)), new Vector2i(6, 5)), Is.True);
        });
    }

    /// <summary>
    /// Two dug tiles make a trench on -1 walled with earth all around, and a roof frame in it lays concrete back over it
    /// </summary>
    [Test]
    public async Task TrenchAndRoofTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var zLevels = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>();
            var maps = SEntMan.System<SharedMapSystem>();
            var tileMan = Server.ResolveDependency<ITileDefinitionManager>();

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            Assert.That(trench.DigTrench(levels[0], new Vector2i(5, 5)));
            Assert.That(trench.DigTrench(levels[0], new Vector2i(6, 5)));

            Assert.That(zLevels.TryGetLayerGrid(levels[0], out var ground));
            Assert.That(maps.GetTileRef(ground!.Value.Owner, ground.Value.Comp, new Vector2i(5, 5)).Tile.IsEmpty, "the trench is not open to the level above");

            var walls = new HashSet<Vector2i>();
            var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.MapUid == levels[-1] && meta.EntityPrototype?.ID == "ST14TrenchWall")
                    walls.Add(maps.TileIndicesFor(levels[-1], SComp<MapGridComponent>(levels[-1]), xform.Coordinates));
            }

            var expected = new HashSet<Vector2i>();
            for (var x = 4; x <= 7; x++)
            for (var y = 4; y <= 6; y++)
            {
                if (y != 5 || x is 4 or 7)
                    expected.Add(new Vector2i(x, y));
            }

            Assert.That(walls, Is.EquivalentTo(expected), "the trench is not walled all around and open along its length");

            SEntMan.SpawnEntity("ST14BunkerRoofFrame", new EntityCoordinates(levels[-1], new Vector2(5.5f, 5.5f)));
            var roof = maps.GetTileRef(ground.Value.Owner, ground.Value.Comp, new Vector2i(5, 5)).Tile;
            Assert.That(roof.TypeId, Is.EqualTo(tileMan["FloorReinforced"].TileId), "the roof frame did not cover the dug-out");
        });
    }

    /// <summary>
    /// A new trench gets steps on its first tile climbing up to the digger; digging the tile they lead to moves them on
    /// </summary>
    [Test]
    public async Task TrenchStepsTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var maps = SEntMan.System<SharedMapSystem>();

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            var grid = SComp<MapGridComponent>(levels[-1]);

            List<(Vector2i Tile, Direction Facing)> Steps()
            {
                var found = new List<(Vector2i, Direction)>();
                var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (xform.MapUid == levels[-1] && meta.EntityPrototype?.ID == "ST14TrenchSteps")
                        found.Add((maps.TileIndicesFor(levels[-1], grid, xform.Coordinates), xform.LocalRotation.GetCardinalDir()));
                }

                return found;
            }

            // the digger stands north of the tile: the steps climb north (a ladder facing south)
            Assert.That(trench.DigTrench(levels[0], new Vector2i(5, 5), new EntityCoordinates(levels[0], new Vector2(5.5f, 6.5f))));
            Assert.That(Steps(), Is.EquivalentTo(new[] { (new Vector2i(5, 5), Direction.South) }), "a new trench has no steps up to the digger");

            // going on east along the trench: no new steps
            Assert.That(trench.DigTrench(levels[0], new Vector2i(6, 5), new EntityCoordinates(levels[0], new Vector2(7.5f, 5.5f))));
            Assert.That(Steps(), Has.Count.EqualTo(1), "a trench dug on got a second set of steps");

            // digging where the steps lead moves them there
            Assert.That(trench.DigTrench(levels[0], new Vector2i(5, 6), new EntityCoordinates(levels[0], new Vector2(5.5f, 7.5f))));
            Assert.That(Steps(), Is.EquivalentTo(new[] { (new Vector2i(5, 6), Direction.South) }), "the steps did not move on to the new tile");
        });
    }

    /// <summary>
    /// A soldier in a new trench walks up its steps and out onto the ground
    /// </summary>
    [Test]
    public async Task ClimbOutOfTrenchTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid trooper = default;

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            Assert.That(trench.DigTrench(levels[0], new Vector2i(5, 5), new EntityCoordinates(levels[0], new Vector2(5.5f, 6.5f))));
            // the low end of the steps, at the trench side
            trooper = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[-1], 5.5f, 5.05f));
        });
        await RunTicksSync(10);

        // a step onto the low end wakes the body: it stays down in the trench
        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetWorldPosition(trooper, new Vector2(5.5f, 5.1f)));
        await RunTicksSync(30);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<TransformComponent>(trooper).MapUid, Is.EqualTo(levels[-1]), "the steps climb the wrong way"));

        // walk north up the steps
        foreach (var y in new[] { 5.4f, 5.6f, 5.8f, 5.95f })
        {
            await Server.WaitPost(() =>
            {
                if (SEntMan.GetComponent<TransformComponent>(trooper).MapUid == levels[-1])
                    SEntMan.System<SharedTransformSystem>().SetWorldPosition(trooper, new Vector2(5.5f, y));
            });
            await RunTicksSync(15);
        }

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(trooper).MapUid, Is.EqualTo(levels[0]), "the steps did not lift the soldier to the ground");
            SEntMan.System<SharedTransformSystem>().SetWorldPosition(trooper, new Vector2(5.5f, 6.5f));
        });
        await RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(trooper).MapUid, Is.EqualTo(levels[0]), "fell back into the trench");
            Assert.That(SComp<ST14ZPhysicsComponent>(trooper).CurrentZLevel, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task SaveAndLoadAllLevelsTest()
    {
        var folder = new ResPath("/ZNetworkSaves/ST14ZLayersTest");
        var depths = new[] { -2, -1, 0, 1, 2 };

        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var save = SEntMan.System<ST14ZNetworkSaveSystem>();
            var maps = SEntMan.System<SharedMapSystem>();
            var tileMan = Server.ResolveDependency<ITileDefinitionManager>();
            var marker = new Tile(tileMan["FloorSteel"].TileId);

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");

            // a mark on every level, at a tile that only that level gets
            foreach (var (depth, level) in levels)
            {
                Assert.That(layers.GetLevels(level).Count, Is.EqualTo(5));
                var grid = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>().TryGetLayerGrid(level, out var g) ? g!.Value : default;
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(3, 3 + depth + 2), marker);
            }

            Assert.That(save.TrySaveNetwork(levels[0], folder), Is.True);
            Assert.That(save.HasSave(folder));

            foreach (var level in levels.Values)
            {
                SEntMan.DeleteEntity(level);
            }
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var save = SEntMan.System<ST14ZNetworkSaveSystem>();
            var zLevels = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>();
            var maps = SEntMan.System<SharedMapSystem>();
            var tileMan = Server.ResolveDependency<ITileDefinitionManager>();

            Assert.That(save.TryLoadNetwork(folder, out var loaded), Is.True);
            Assert.That(loaded!.Keys.OrderBy(d => d), Is.EqualTo(depths));

            foreach (var (depth, level) in loaded)
            {
                Assert.That(SComp<ST14ZMapComponent>(level).Depth, Is.EqualTo(depth));
                Assert.That(zLevels.TryGetLayerGrid(level, out var grid));
                var tile = maps.GetTileRef(grid!.Value.Owner, grid.Value.Comp, new Vector2i(3, 3 + depth + 2)).Tile;
                Assert.That(tile.TypeId, Is.EqualTo(tileMan["FloorSteel"].TileId), $"mark of level {depth} after load");
            }

            Assert.That(SComp<ST14ZMapComponent>(loaded[0]).MapAbove, Is.EqualTo((EntityUid?) loaded[1].Owner));
            Assert.That(SComp<ST14ZMapComponent>(loaded[0]).MapBelow, Is.EqualTo((EntityUid?) loaded[-1].Owner));

            foreach (var level in loaded.Values)
            {
                SEntMan.DeleteEntity(level);
            }

            Server.ResolveDependency<IResourceManager>().UserData.Delete(folder);
        });
    }

    /// <summary>
    /// What the five levels cost on the real sector map: time to build, tiles and entities per level, save size
    /// </summary>
    [Test, Explicit]
    public async Task TemplateSectorLayersCost()
    {
        await Server.WaitAssertion(() =>
        {
            var loader = SEntMan.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var save = SEntMan.System<ST14ZNetworkSaveSystem>();
            var zLevels = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>();
            var resources = Server.ResolveDependency<IResourceManager>();

            // loaded the way a sector round loads a fresh sector
            Assert.That(loader.TryLoadMap(new ResPath("/Maps/_ST14/Sectors/template.yml"), out var map, out _,
                new Robust.Shared.EntitySerialization.DeserializationOptions { InitializeMaps = true }));

            var entitiesBefore = SEntMan.EntityCount;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var levels = layers.EnsureLayers(map!.Value, "ST14PlanetSector");
            sw.Stop();
            TestContext.Out.WriteLine($"ZLAYERS build 4 levels on template: {sw.Elapsed.TotalMilliseconds:F1} ms, +{SEntMan.EntityCount - entitiesBefore} entities");

            foreach (var (depth, level) in levels.OrderBy(kv => kv.Key))
            {
                var tiles = 0;
                if (zLevels.TryGetLayerGrid(level, out var grid))
                {
                    var e = SEntMan.System<SharedMapSystem>().GetAllTilesEnumerator(grid.Value.Owner, grid.Value.Comp);
                    while (e.MoveNext(out _))
                        tiles++;
                }

                var entities = 0;
                var q = SEntMan.AllEntityQueryEnumerator<TransformComponent>();
                while (q.MoveNext(out var xform))
                {
                    if (xform.MapUid == level)
                        entities++;
                }

                TestContext.Out.WriteLine($"ZLAYERS level {depth}: {tiles} tiles, {entities} entities");
            }

            var folder = new ResPath("/ZNetworkSaves/ST14ZLayersCost");
            sw.Restart();
            Assert.That(save.TrySaveNetwork(map.Value, folder));
            sw.Stop();
            long total = 0;
            foreach (var depth in levels.Keys.OrderBy(d => d))
            {
                var file = folder / ST14ZNetworkSaveSystem.FileName(depth);
                using var stream = resources.UserData.OpenRead(file);
                total += stream.Length;
                TestContext.Out.WriteLine($"ZLAYERS save level {depth}: {stream.Length} bytes");
            }

            TestContext.Out.WriteLine($"ZLAYERS save all: {sw.Elapsed.TotalMilliseconds:F1} ms, {total} bytes");
            resources.UserData.Delete(folder);
        });
    }

    [Test]
    public async Task SameWeatherOnAllLevelsTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var weather = SEntMan.System<ST14SectorWeatherSystem>();
            var proto = SProtoMan.EnumeratePrototypes<ST14WeatherEventPrototype>().First();

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");

            // started from a level that is not the main one: the sector has one sky
            Assert.That(weather.Start(levels[1], proto), Is.True);

            var main = SComp<ST14SectorWeatherComponent>(levels[0]);
            foreach (var (depth, level) in levels)
            {
                Assert.That(STryComp<ST14SectorWeatherComponent>(level, out var sky), $"no weather on level {depth}");
                Assert.That(sky!.Event, Is.EqualTo(main.Event));
                Assert.That(sky.Start, Is.EqualTo(main.Start));
                Assert.That(sky.End, Is.EqualTo(main.End));
                Assert.That(sky.Wind, Is.EqualTo(main.Wind));
            }

            Assert.That(weather.Current(levels[-2])?.ID, Is.EqualTo(proto.ID));
        });
    }
}
