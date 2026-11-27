#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.Weather;
using Content.Server._ST14.ZLevels.Layers;
using Content.Shared._ST14.Weather;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Fortifications;
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
    /// The levels a test builds stay on the server: a pair that ran one is not handed on to the next test, whose
    /// lookups would otherwise run into a level being torn down (seen as "Can't resolve BroadphaseComponent" there)
    /// </summary>
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

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

- type: entity
  id: ST14ZLayersBigDummy
  components:
  - type: ST14ZPhysics
  - type: Physics
    bodyType: Dynamic
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.9
        density: 100
        mask:
        - MobMask
        layer:
        - MobLayer
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
            var roofs = SEntMan.System<Content.Server._ST14.ZLevels.Roof.ST14ZLevelsRoofSystem>();
            Assert.That(roofs.TryGetRoofEntity(levels[0], new Vector2i(5, 5), out _), "the roof frame did not cover the dug-out");
            Assert.That(roofs.TryGetRoofEntity(levels[0], new Vector2i(6, 5), out _), "the roof frame did not cover the whole dug-out");
            Assert.That(roofs.TryGetRoofEntity(levels[0], new Vector2i(5, 6), out _), Is.False, "a roof was put on the ground");

            var below = levels[-1];
            var rooved = SEntMan.System<SharedRoofSystem>();
            Assert.That(rooved.IsRooved((below, SComp<MapGridComponent>(below), SComp<RoofComponent>(below)), new Vector2i(5, 5)), "the bunker roof does not keep the sky off the dug-out");
        });
    }

    /// <summary>
    /// The entrenching tool lays out a segment and it opens all at once when dug: a trench stays open to the sky, a
    /// dug-out gets its roof. A layout over a rock or another layout is refused
    /// </summary>
    [Test]
    public async Task DigSegmentsTest()
    {
        EntityUid sector = default;
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var roofs = SEntMan.System<Content.Server._ST14.ZLevels.Roof.ST14ZLevelsRoofSystem>();
            var zLevels = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>();
            var maps = SEntMan.System<SharedMapSystem>();

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            Assert.That(zLevels.TryGetLayerGrid(levels[0], out var ground));
            bool Dug(Vector2i tile) => maps.GetTileRef(ground!.Value.Owner, ground.Value.Comp, tile).Tile.IsEmpty;

            var line = new[] { new Vector2i(5, 5), new Vector2i(6, 5), new Vector2i(7, 5) };
            var segment = trench.TryLayOut(levels[0], line, false, 3f);
            Assert.That(segment, Is.Not.Null, "the trench was not laid out");
            Assert.That(trench.TryLayOut(levels[0], new[] { new Vector2i(7, 5), new Vector2i(8, 5) }, false, 3f), Is.Null,
                "a layout went over another one");

            // a layout side by side with another joins its string: no string between the two
            var joined = trench.TryLayOut(levels[0], new[] { new Vector2i(5, 6), new Vector2i(6, 6), new Vector2i(7, 6) }, false, 3f);
            Assert.That(joined, Is.Not.Null);
            var looks = SEntMan.System<SharedAppearanceSystem>();
            var lower = SComp<ST14TrenchBlueprintComponent>(segment!.Value).Markers[1];
            Assert.That(looks.TryGetData<bool>(lower, ST14OutlineVisuals.North, out var across) && across, Is.False, "two layouts side by side are strung apart");
            SEntMan.DeleteEntity(joined);
            Assert.That(looks.TryGetData<bool>(lower, ST14OutlineVisuals.North, out across) && across, "a layout taken away left a gap in the string next to it");
            Assert.That(line.Any(Dug), Is.False, "laying out dug the ground");

            var blueprint = SComp<ST14TrenchBlueprintComponent>(segment!.Value);
            Assert.That(blueprint.Total, Is.EqualTo(9f));
            Assert.That(blueprint.Markers, Has.Count.EqualTo(3), "not every tile of the layout is staked");
            var markers = blueprint.Markers.ToList();

            // one string round the segment: the middle stake of the line has string along its sides, none across
            var appearance = SEntMan.System<SharedAppearanceSystem>();
            bool Shown(EntityUid uid, ST14OutlineVisuals key) => appearance.TryGetData<bool>(uid, key, out var shown) && shown;
            Assert.That(Shown(markers[1], ST14OutlineVisuals.North) && Shown(markers[1], ST14OutlineVisuals.South), "the layout has no string along it");
            Assert.That(Shown(markers[1], ST14OutlineVisuals.East) || Shown(markers[1], ST14OutlineVisuals.West), Is.False, "the layout is strung tile by tile");
            Assert.That(Shown(markers[0], ST14OutlineVisuals.NorthWest) || Shown(markers[0], ST14OutlineVisuals.NorthEast) || Shown(markers[0], ST14OutlineVisuals.SouthWest) || Shown(markers[0], ST14OutlineVisuals.SouthEast), "the end of the layout has no stake");

            trench.FinishBlueprint((segment.Value, blueprint));
            Assert.That(line.All(Dug), "the segment did not open all at once");
            Assert.That(line.Any(t => roofs.TryGetRoofEntity(levels[0], t, out _)), Is.False, "a trench got a roof");
            Assert.That(markers.All(m => SEntMan.Deleted(m) || SEntMan.IsQueuedForDeletion(m)), "the stakes stayed after digging");

            // the wooden rim: one frame round the dug line, planks along it and not between its tiles
            var rims = new Dictionary<Vector2i, EntityUid>();
            var rimQuery = SEntMan.AllEntityQueryEnumerator<ST14TrenchRimComponent, TransformComponent>();
            while (rimQuery.MoveNext(out var rimUid, out _, out var rimXform))
            {
                if (rimXform.MapUid != levels[0] || SEntMan.IsQueuedForDeletion(rimUid))
                    continue;

                var p = SEntMan.System<SharedTransformSystem>().GetWorldPosition(rimXform);
                rims[new Vector2i((int) MathF.Floor(p.X), (int) MathF.Floor(p.Y))] = rimUid;
            }

            Assert.That(rims.Keys, Is.EquivalentTo(line), "not every dug tile has a rim");
            Assert.That(Shown(rims[new Vector2i(6, 5)], ST14OutlineVisuals.North) && !Shown(rims[new Vector2i(6, 5)], ST14OutlineVisuals.East),
                "the rim does not run along the trench");
            Assert.That(Shown(rims[new Vector2i(7, 5)], ST14OutlineVisuals.East), "the end of the trench has no rim");

            // a 3x3 dug-out whose west door is the end of the trench: closed all round but there, roofed when dug
            var square = new List<Vector2i>();
            for (var x = 8; x <= 10; x++)
            for (var y = 4; y <= 6; y++)
                square.Add(new Vector2i(x, y));

            Assert.That(trench.TryLayOut(levels[0], square.Select(t => t + new Vector2i(0, 1)).ToList(), true, 3f), Is.Null,
                "a dug-out was laid out against the side of a trench, not at a door");
            var dugout = trench.TryLayOut(levels[0], square, true, 3f);
            Assert.That(dugout, Is.Not.Null, "the dug-out was not laid out with the trench at its door");
            trench.FinishBlueprint((dugout!.Value, SComp<ST14TrenchBlueprintComponent>(dugout.Value)));
            Assert.That(square.All(Dug));
            foreach (var tile in square)
            {
                Assert.That(roofs.TryGetRoofEntity(levels[0], tile, out _), $"the dug-out at {tile} has no roof");
            }

            // a trench may come up to it only at the middle of a side
            Assert.That(trench.TryLayOut(levels[0], new[] { new Vector2i(8, 7), new Vector2i(8, 8) }, false, 3f), Is.Null,
                "a trench was dug into the side of a dug-out, not at a door");
            var door = trench.TryLayOut(levels[0], new[] { new Vector2i(9, 7), new Vector2i(9, 8) }, false, 3f);
            Assert.That(door, Is.Not.Null, "a trench cannot be dug up to the door of a dug-out");
            SEntMan.DeleteEntity(door);

            // two dug-outs wall to wall are one building
            var next = new List<Vector2i>();
            for (var x = 8; x <= 10; x++)
            for (var y = 7; y <= 9; y++)
                next.Add(new Vector2i(x, y));

            var neighbour = trench.TryLayOut(levels[0], next, true, 3f);
            Assert.That(neighbour, Is.Not.Null, "a dug-out cannot be laid out wall to wall with another");
            trench.FinishBlueprint((neighbour!.Value, SComp<ST14TrenchBlueprintComponent>(neighbour.Value)));
            var seams = 0;
            var seamQuery = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (seamQuery.MoveNext(out _, out var seamMeta, out var seamXform))
            {
                if (seamXform.MapUid == levels[-1] && seamMeta.EntityPrototype?.ID == "ST14DugoutSeam")
                    seams++;
            }

            // the two dug-outs are one building with nothing between them; the trench at the door of the first is closed off
            // by a partition: no door by itself
            Assert.That(seams, Is.EqualTo(1), "dug-outs side by side are walled apart, or the trench opens into one by itself");

            // an L-shaped layout: where it turns inward the string bends round a stake
            var corner = trench.TryLayOut(levels[0], new[] { new Vector2i(2, 2), new Vector2i(3, 2), new Vector2i(2, 3) }, false, 3f);
            Assert.That(corner, Is.Not.Null);
            var bend = SComp<ST14TrenchBlueprintComponent>(corner!.Value).Markers[0];
            Assert.That(SEntMan.System<SharedAppearanceSystem>().TryGetData<bool>(bend, ST14OutlineVisuals.InnerNorthEast, out var inner) && inner,
                "the string has a gap where the layout turns");

            // a rock in the way spoils the layout
            var rock = SEntMan.SpawnEntity("ST14TrenchWall", new EntityCoordinates(ground!.Value.Owner, new Vector2(3.5f, 9.5f)));
            Assert.That(SEntMan.GetComponent<TransformComponent>(rock).Anchored, "the wall did not stand on the ground");
            sector = levels[0];
        });
        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            Assert.That(trench.TryLayOut(sector, new[] { new Vector2i(2, 9), new Vector2i(3, 9) }, false, 3f), Is.Null,
                "a layout went over a wall");
        });
    }

    /// <summary>
    /// Steps are cut into a trench with the tool: at its end they climb out over the end. Cutting again fills them in
    /// </summary>
    [Test]
    public async Task CutStepsTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var maps = SEntMan.System<SharedMapSystem>();

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            var grid = SComp<MapGridComponent>(levels[-1]);
            for (var x = 5; x <= 7; x++)
            {
                Assert.That(trench.DigTrench(levels[0], new Vector2i(x, 5)));
            }

            List<(Vector2i Tile, Direction Facing)> Steps()
            {
                var found = new List<(Vector2i, Direction)>();
                var query = SEntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var meta, out var xform))
                {
                    if (xform.MapUid == levels[-1] && meta.EntityPrototype?.ID == "ST14TrenchSteps" && !SEntMan.IsQueuedForDeletion(uid))
                        found.Add((maps.TileIndicesFor(levels[-1], grid, xform.Coordinates), xform.LocalRotation.GetCardinalDir()));
                }

                return found;
            }

            Assert.That(Steps(), Is.Empty, "a dug trench came with steps");
            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(9, 5)), Is.Null, "steps were cut into the ground");

            // the west end: they climb west, a ladder facing east
            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(5, 5)), Is.True);
            Assert.That(Steps(), Is.EquivalentTo(new[] { (new Vector2i(5, 5), Direction.East) }), "the steps do not climb out over the end");

            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(5, 5)), Is.False);
            Assert.That(Steps(), Is.Empty, "the steps were not filled in");

            // turned from the build window: north out of the middle, then turned south, then the same way fills them in
            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(6, 5), new Vector2i(0, 1)), Is.True);
            Assert.That(Steps(), Is.EquivalentTo(new[] { (new Vector2i(6, 5), Direction.South) }), "the steps do not climb north");
            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(6, 5), new Vector2i(0, -1)), Is.True);
            Assert.That(Steps(), Is.EquivalentTo(new[] { (new Vector2i(6, 5), Direction.North) }), "the steps were not turned south");
            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(6, 5), new Vector2i(1, 0)), Is.Null, "steps lead into the trench");
            Assert.That(trench.ToggleSteps(levels[0], new Vector2i(6, 5), new Vector2i(0, -1)), Is.False);
            Assert.That(Steps(), Is.Empty);
        });
    }

    /// <summary>
    /// The ghost of the build window and the server lay out the same tiles, turned with R
    /// </summary>
    [Test]
    public void TrenchShapesTest()
    {
        var o = new Vector2i(10, 10);
        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Trench, o, Direction.South),
            Is.EquivalentTo(new[] { new Vector2i(10, 11), o, new Vector2i(10, 9) }), "a trench is not centred on the cursor");
        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Trench, o, Direction.East),
            Is.EquivalentTo(new[] { new Vector2i(9, 10), o, new Vector2i(11, 10) }), "a turned trench does not run east-west");
        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Dugout, o, Direction.South), Has.Count.EqualTo(9), "a dug-out is not 3x3");
        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Dugout, o, Direction.South), Does.Contain(o));
        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Steps, o, Direction.North), Is.EquivalentTo(new[] { o }));

        // a dug-out is centred on the tile under the cursor
        var around = new List<Vector2i>();
        for (var x = 9; x <= 11; x++)
        for (var y = 9; y <= 11; y++)
            around.Add(new Vector2i(x, y));

        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Dugout, new Vector2(10.5f, 10.5f), Direction.South), Is.EquivalentTo(around), "a dug-out is not centred on the cursor");
        Assert.That(ST14TrenchShapes.Shape(ST14DigMode.Dugout, new Vector2(10.9f, 10.1f), Direction.South), Is.EquivalentTo(around), "a dug-out is not centred on the cursor");

        // the door goes in the middle of the side nearest the cursor
        (Vector2i Center, int Size)? DugoutAt(Vector2i t) => around.Contains(t) ? (new Vector2i(10, 10), 3) : null;
        Assert.That(ST14DugoutDoors.TryFind(new Vector2(12.4f, 10.2f), DugoutAt, (_, _) => true, out var spots, out var door));
        Assert.That(spots, Is.EquivalentTo(new[] { new Vector2i(10, 12), new Vector2i(10, 8), new Vector2i(12, 10), new Vector2i(8, 10) }));
        Assert.That(door, Is.EqualTo(new Vector2i(12, 10)), "the door is not on the side nearest the cursor");

        // where a door cannot go it is not offered: the next side is, and with none at all nothing is
        Assert.That(ST14DugoutDoors.TryFind(new Vector2(12.4f, 10.2f), DugoutAt, (spot, _) => spot != new Vector2i(12, 10), out var left, out var other));
        Assert.That(left, Has.Length.EqualTo(3), "a door spot that cannot take a door is still offered");
        Assert.That(other, Is.Not.EqualTo(new Vector2i(12, 10)));
        Assert.That(ST14DugoutDoors.TryFind(new Vector2(12.4f, 10.2f), DugoutAt, (_, _) => false, out _, out _), Is.False,
            "a door is offered where none can go");
        Assert.That(ST14DugoutDoors.TryFind(new Vector2(20.5f, 20.5f), DugoutAt, (_, _) => true, out _, out _), Is.False, "a door was found with no dug-out near");
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
    /// A bunker wall is climbed with Alt+click: on the level above the soldier stands on the wall top and walks over
    /// the roof without falling through it
    /// </summary>
    [Test]
    public async Task ClimbOntoBunkerTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid onRoof = default;
        EntityUid onWall = default;
        EntityUid wall = default;

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var zLevels = SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            Assert.That(zLevels.TryGetLayerGrid(levels[0], out var ground));

            for (var x = 5; x <= 7; x++)
            for (var y = 5; y <= 7; y++)
            {
                if (x != 6 || y != 6)
                    SEntMan.SpawnEntity("ST14BunkerWall", new EntityCoordinates(ground!.Value.Owner, new Vector2(x + 0.5f, y + 0.5f)));
            }

            wall = SEntMan.SpawnEntity("ST14BunkerWall", new EntityCoordinates(ground!.Value.Owner, new Vector2(4.5f, 6.5f)));
            SEntMan.SpawnEntity("ST14BunkerRoofFrame", new EntityCoordinates(ground.Value.Owner, new Vector2(6.5f, 6.5f)));
        });
        await RunTicksSync(5);

        await Server.WaitPost(() =>
        {
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            Assert.That(trench.TryGetClimbSpot(wall, out _), "a bunker wall cannot be climbed");
            onRoof = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[1], new Vector2(6.5f, 6.5f)));
            onWall = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[1], new Vector2(4.5f, 6.5f)));
        });
        await RunTicksSync(10);

        // a step wakes the bodies: a body only falls once it moves
        await Server.WaitPost(() =>
        {
            var xforms = SEntMan.System<SharedTransformSystem>();
            xforms.SetWorldPosition(onRoof, new Vector2(6.4f, 6.5f));
            xforms.SetWorldPosition(onWall, new Vector2(4.4f, 6.5f));
        });
        await RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(onRoof).MapUid, Is.EqualTo(levels[1]), "fell through the bunker roof");
            Assert.That(SEntMan.GetComponent<TransformComponent>(onWall).MapUid, Is.EqualTo(levels[1]), "fell off the bunker wall top");
        });
    }

    /// <summary>
    /// A dug-out's roof sticks up out of the ground: it blocks the way on the surface, is climbed onto with Alt+click
    /// and holds whoever stands on it a level up
    /// </summary>
    [Test]
    public async Task ClimbOntoDugoutTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid roof = default;
        EntityUid onTop = default;

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var roofs = SEntMan.System<Content.Server._ST14.ZLevels.Roof.ST14ZLevelsRoofSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");

            var square = new[] { new Vector2i(5, 5), new Vector2i(6, 5), new Vector2i(5, 6), new Vector2i(6, 6) };
            var dugout = trench.TryLayOut(levels[0], square, true, 3f);
            Assert.That(dugout, Is.Not.Null);
            trench.FinishBlueprint((dugout!.Value, SComp<ST14TrenchBlueprintComponent>(dugout.Value)));

            Assert.That(roofs.TryGetRoofEntity(levels[0], new Vector2i(5, 5), out var found), "the dug-out has no roof");
            roof = found!.Value;
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(roof).EntityPrototype?.ID, Is.EqualTo("ST14DugoutRoof"));

            // a raised block: its log side and rims show only round the whole roof
            var looks = SEntMan.System<SharedAppearanceSystem>();
            bool Edge(EntityUid uid, ST14OutlineVisuals key) => looks.TryGetData<bool>(uid, key, out var shown) && shown;
            Assert.That(Edge(roof, ST14OutlineVisuals.South) && Edge(roof, ST14OutlineVisuals.West), "the dug-out roof has no edge on the outside");
            Assert.That(Edge(roof, ST14OutlineVisuals.North) || Edge(roof, ST14OutlineVisuals.East), Is.False, "the dug-out roof has edges between its tiles");

            var layer = 0;
            foreach (var fixture in SComp<Robust.Shared.Physics.FixturesComponent>(roof).Fixtures.Values)
            {
                layer |= fixture.CollisionLayer;
            }

            Assert.That(layer & (int) Content.Shared.Physics.CollisionGroup.MidImpassable, Is.Not.Zero, "the dug-out roof can be walked onto");
            Assert.That(trench.TryGetClimbSpot(roof, out _), "the dug-out roof cannot be climbed");
            onTop = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[1], new Vector2(5.5f, 5.5f)));
        });
        await RunTicksSync(10);

        await Server.WaitPost(() => SEntMan.System<SharedTransformSystem>().SetWorldPosition(onTop, new Vector2(6.4f, 5.5f)));
        await RunTicksSync(90);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<TransformComponent>(onTop).MapUid, Is.EqualTo(levels[1]), "fell off the top of the dug-out roof"));
    }

    /// <summary>
    /// Only what fits through an opening falls into it: a body wider than a trench one tile wide stays on its edge, the
    /// same body over a 3x3 pit drops into it
    /// </summary>
    [Test]
    public async Task TooBigToFallInTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid overTrench = default;
        EntityUid overPit = default;
        EntityUid warrior = default;
        EntityUid larva = default;

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");

            // a trench one tile wide running north, and a 3x3 pit
            for (var y = 2; y <= 5; y++)
            {
                trench.DigTrench(levels[0], new Vector2i(3, y));
            }

            // and another for the bugs
            for (var y = 2; y <= 5; y++)
            {
                trench.DigTrench(levels[0], new Vector2i(6, y));
            }

            for (var x = 7; x <= 9; x++)
            for (var y = 6; y <= 8; y++)
                trench.DigTrench(levels[0], new Vector2i(x, y));

            overTrench = SEntMan.SpawnEntity("ST14ZLayersBigDummy", new EntityCoordinates(levels[0], new Vector2(3.5f, 3.5f)));
            overPit = SEntMan.SpawnEntity("ST14ZLayersBigDummy", new EntityCoordinates(levels[0], new Vector2(8.5f, 7.5f)));
            warrior = SEntMan.SpawnEntity("ST14MobBugWarrior", new EntityCoordinates(levels[0], new Vector2(6.5f, 3.5f)));
            larva = SEntMan.SpawnEntity("ST14MobBugLarva", new EntityCoordinates(levels[0], new Vector2(6.5f, 5.5f)));
        });
        await RunTicksSync(10);

        // a step wakes the bodies: a body only falls once it moves
        await Server.WaitPost(() =>
        {
            var xforms = SEntMan.System<SharedTransformSystem>();
            xforms.SetWorldPosition(overTrench, new Vector2(3.5f, 3.6f));
            xforms.SetWorldPosition(overPit, new Vector2(8.5f, 7.6f));
            xforms.SetWorldPosition(warrior, new Vector2(6.5f, 3.6f));
            xforms.SetWorldPosition(larva, new Vector2(6.5f, 5.4f));
        });
        await RunTicksSync(90);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(overTrench).MapUid, Is.EqualTo(levels[0]),
                "a body wider than the trench fell into it");
            Assert.That(SEntMan.GetComponent<TransformComponent>(overPit).MapUid, Is.EqualTo(levels[-1]),
                "a body that fits the pit did not fall into it");
            Assert.That(SEntMan.GetComponent<TransformComponent>(warrior).MapUid, Is.EqualTo(levels[0]), "a warrior bug fell into a trench one tile wide");
            Assert.That(SEntMan.GetComponent<TransformComponent>(larva).MapUid, Is.EqualTo(levels[-1]), "a larva did not fall into the trench");
        });
    }

    /// <summary>
    /// A blow from the edge of a trench reaches whoever is down in it, through the opening: the one under the cursor is
    /// picked, in reach; too far off, or under a roof, they are not
    /// </summary>
    [Test]
    public async Task MeleeIntoTrenchTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var reach = SEntMan.System<Content.Shared._ST14.ZLevels.Core.EntitySystems.ST14SharedZLevelsSystem>();
            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            trench.DigTrench(levels[0], new Vector2i(5, 5));
            trench.DigTrench(levels[0], new Vector2i(8, 5));

            var target = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(5.5f, 5.5f)));
            var onEdge = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[0], new Vector2(5.5f, 6.5f)));
            var farOff = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[0], new Vector2(5.5f, 9.5f)));
            var surfaceId = SComp<MapComponent>(levels[0]).MapId;

            Assert.That(reach.EntityBelowThrough(new MapCoordinates(new Vector2(5.5f, 5.6f), surfaceId)), Is.EqualTo(target),
                "the cursor over the trench does not pick whoever is in it");
            Assert.That(reach.EntityBelowThrough(new MapCoordinates(new Vector2(5.5f, 7.5f), surfaceId)), Is.Null,
                "something was picked under solid ground");
            Assert.That(reach.InReachAcrossLevels(onEdge, target, 1.5f), "a blow from the edge does not reach into the trench");
            Assert.That(reach.InReachAcrossLevels(target, onEdge, 1.5f), "a blow from the trench does not reach the edge");
            Assert.That(reach.InReachAcrossLevels(farOff, target, 1.5f), Is.False, "a blow reached into the trench from afar");

            // under a roof the opening is closed
            var other = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(8.5f, 5.5f)));
            var nextTo = SEntMan.SpawnEntity("ST14ZLayersDummy", new EntityCoordinates(levels[0], new Vector2(8.5f, 6.5f)));
            trench.PlaceRoofs(levels[0], new[] { new Vector2i(8, 5) });
            Assert.That(reach.InReachAcrossLevels(nextTo, other, 1.5f), Is.False, "a blow went through a roof");
        });
    }

    /// <summary>
    /// Bunker upgrades stand on the roof: built from the floor or the roof of a bunker they go over its middle, the inside
    /// stays free. One a bunker; a base wants a structure of six and no upgrade next to it; a ramp wants open ground
    /// past its side. A garrison puts its gun up there and fires at the bugs on its own (the machine gun only within its
    /// arc, the heavy gun only at the big ones). The ramp and the ladder take a trooper between the ground or the roof
    /// and the inside. The tower lets whoever is inside zoom out. Taken down, each takes what it put in place.
    /// </summary>
    [Test]
    public async Task BunkerUpgradesTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid observer = default;
        EntityUid garrison = default;
        EntityUid baseUpgrade = default;
        EntityUid ramp = default;
        EntityUid ladder = default;
        EntityUid target = default;
        Vector2 maxZoomBefore = default;

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var transform = SEntMan.System<SharedTransformSystem>();
            var lookup = SEntMan.System<EntityLookupSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");

            void Bunker(Vector2i center)
            {
                var square = new List<Vector2i>();
                for (var x = -1; x <= 1; x++)
                for (var y = -1; y <= 1; y++)
                    square.Add(center + new Vector2i(x, y));

                var layout = trench.TryLayOut(levels[0], square, true, 3f);
                Assert.That(layout, Is.Not.Null, $"the bunker at {center} was not laid out");
                trench.FinishBlueprint((layout!.Value, SComp<ST14TrenchBlueprintComponent>(layout.Value)));
            }

            EntityCoordinates Floor(float x, float y) => new(levels[-1], new Vector2(x, y));
            EntityCoordinates Roof(float x, float y) => new(levels[0], new Vector2(x, y));
            Vector2 At(EntityUid uid) => transform.GetWorldPosition(uid);
            EntityUid? MapOf(EntityUid uid) => SEntMan.GetComponent<TransformComponent>(uid).MapUid;

            // a bunker structure of five, two rows, the second one short
            foreach (var center in new[] { new Vector2i(2, 2), new Vector2i(5, 2), new Vector2i(8, 2), new Vector2i(2, 5), new Vector2i(5, 5) })
            {
                Bunker(center);
            }

            var upgrade = new ST14DugoutUpgradeCondition();
            var baseCondition = new ST14DugoutUpgradeCondition { Base = true, Apart = true };
            var apart = new ST14DugoutUpgradeCondition { Apart = true };
            var rampCondition = new ST14DugoutUpgradeCondition { Ramp = true };

            Assert.That(upgrade.Condition(default, Floor(2.5f, 2.5f), Direction.South), "an upgrade cannot be built from inside a bunker");
            Assert.That(upgrade.Condition(default, Roof(1.5f, 3.5f), Direction.South), "an upgrade cannot be built from the roof of a bunker");
            Assert.That(upgrade.Condition(default, Floor(10.5f, 9.5f), Direction.South), Is.False, "an upgrade went off a bunker");
            Assert.That(baseCondition.Condition(default, Floor(2.5f, 5.5f), Direction.South), Is.False, "a base went into a structure of five");

            // the blueprint goes over the middle of the bunker, on the level pointed at
            Assert.That(upgrade.TrySnap(Floor(4.5f, 1.5f), Direction.South, out var snapped));
            Assert.That(snapped.EntityId, Is.EqualTo(levels[-1]));
            Assert.That(snapped.Position, Is.EqualTo(new Vector2(5.5f, 2.5f)));
            Assert.That(upgrade.TrySnap(Roof(6.5f, 3.5f), Direction.South, out snapped));
            Assert.That(snapped.EntityId, Is.EqualTo(levels[0]));
            Assert.That(snapped.Position, Is.EqualTo(new Vector2(5.5f, 2.5f)));

            // the sixth makes the structure big enough for a base
            Bunker(new Vector2i(8, 5));
            Assert.That(baseCondition.Condition(default, Floor(2.5f, 5.5f), Direction.South), "a base cannot go into a structure of six");

            // a rifle garrison built inside goes up on the roof, its gun with it; the inside stays empty
            garrison = SEntMan.SpawnEntity("ST14BunkerRifleGarrison", Floor(5.5f, 2.5f));
            Assert.That(MapOf(garrison), Is.EqualTo(levels[0]), "the rifle garrison is not on the roof");
            Assert.That(At(garrison), Is.EqualTo(new Vector2(5.5f, 2.5f)));
            var floorMap = SEntMan.GetComponent<TransformComponent>(levels[-1]).MapID;
            Assert.That(lookup.GetEntitiesInRange<ST14BunkerUpgradeComponent>(new MapCoordinates(new Vector2(5.5f, 2.5f), floorMap), 2f), Is.Empty,
                "something of the garrison stayed inside the bunker");
            var gun = SComp<ST14BunkerGarrisonComponent>(garrison).Gun;
            Assert.That(gun, Is.Not.Null, "the garrison has no gun");
            Assert.That(MapOf(gun!.Value), Is.EqualTo(levels[0]), "the garrison gun is not on the roof");
            Assert.That(upgrade.Condition(default, Floor(4.5f, 1.5f), Direction.South), Is.False, "a second upgrade went on the same bunker from inside");
            Assert.That(upgrade.Condition(default, Roof(6.5f, 3.5f), Direction.South), Is.False, "a second upgrade went on the same bunker from the roof");

            // upgrades that keep apart: not next to the garrison, but fine by bunkers with nothing on them
            Assert.That(apart.Condition(default, Floor(8.5f, 2.5f), Direction.South), Is.False, "an observation bunker went next to a garrison");
            Assert.That(apart.Condition(default, Floor(2.5f, 5.5f), Direction.South), "an observation bunker cannot go among empty bunkers");

            // the base: its supply crate in a corner inside
            baseUpgrade = SEntMan.SpawnEntity("ST14BunkerBase", Floor(2.5f, 5.5f));
            Assert.That(MapOf(baseUpgrade), Is.EqualTo(levels[0]));
            var crate = SComp<ST14BunkerUpgradeComponent>(baseUpgrade).InsideEntity;
            Assert.That(crate, Is.Not.Null, "the base put no supply crate inside");
            Assert.That(MapOf(crate!.Value), Is.EqualTo(levels[-1]));
            Assert.That(At(crate.Value), Is.EqualTo(new Vector2(1.5f, 6.5f)), "the supply crate is not in the corner");
            Assert.That(SEntMan.HasComponent<ST14BunkerBaseComponent>(crate.Value));

            // a firing slit in the middle of the east side of the last bunker, where its earth wall is
            var slit = new ST14DugoutSlitCondition();
            Assert.That(slit.Condition(default, Floor(10.5f, 5.5f), Direction.South), "a slit cannot go in the middle of a side");
            Assert.That(slit.Condition(default, Floor(10.5f, 4.5f), Direction.South), Is.False, "a slit went off the middle of a side");
            SEntMan.SpawnEntity("ST14BunkerFiringSlit", Floor(10.5f, 5.5f));
            var walls = 0;
            var maps = SEntMan.System<SharedMapSystem>();
            var floorGrid = SComp<MapGridComponent>(levels[-1]);
            var anchored = maps.GetAnchoredEntitiesEnumerator(levels[-1], floorGrid, new Vector2i(10, 5));
            while (anchored.MoveNext(out var uid))
            {
                if (SEntMan.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID == "ST14TrenchWall")
                    walls++;
            }

            Assert.That(walls, Is.Zero, "the earth wall stayed where the slit went");

            // a ramp over the east side of the bunker at (8, 2): open ground there, the next bunker to the west
            Assert.That(rampCondition.Condition(default, Floor(8.5f, 2.5f), Direction.East), "a ramp cannot face open ground");
            Assert.That(rampCondition.Condition(default, Floor(8.5f, 2.5f), Direction.West), Is.False, "a ramp went down from another bunker");
            ramp = SEntMan.SpawnAttachedTo("ST14BunkerRamp", Floor(8.5f, 2.5f), rotation: Direction.East.ToAngle());
            var rampComp = SComp<ST14BunkerRampComponent>(ramp);
            Assert.That(rampComp.Outside, Is.Not.Null);
            Assert.That(rampComp.Inside, Is.Not.Null);
            Assert.That(MapOf(rampComp.Outside!.Value), Is.EqualTo(levels[0]));
            Assert.That(At(rampComp.Outside.Value), Is.EqualTo(new Vector2(10.5f, 2.5f)), "the foot of the ramp is not past the east side");
            Assert.That(MapOf(rampComp.Inside!.Value), Is.EqualTo(levels[-1]));
            Assert.That(At(rampComp.Inside.Value), Is.EqualTo(new Vector2(9.5f, 2.5f)), "the top of the ramp is not inside the east side");

            // down the ramp and up again
            var upgrades = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14BunkerUpgradeSystem>();
            var walker = SEntMan.SpawnEntity("MobHuman", Roof(10.5f, 2.5f));
            upgrades.Go(walker, rampComp.Inside.Value);
            Assert.That(MapOf(walker), Is.EqualTo(levels[-1]), "the ramp did not take the trooper down into the bunker");
            Assert.That(At(walker), Is.EqualTo(new Vector2(9.5f, 2.5f)));
            upgrades.Go(walker, rampComp.Outside.Value);
            Assert.That(MapOf(walker), Is.EqualTo(levels[0]), "the ramp did not take the trooper up out of the bunker");

            // a ladder up to the roof, its hatch right over it
            var ladderCondition = new ST14DugoutLadderCondition();
            Assert.That(ladderCondition.Condition(default, Floor(1.5f, 1.5f), Direction.South), "a ladder cannot go inside a bunker");
            Assert.That(ladderCondition.Condition(default, Floor(2.5f, 2.5f), Direction.South), Is.False, "a ladder went in the middle of a bunker");
            ladder = SEntMan.SpawnEntity("ST14BunkerLadder", Floor(1.5f, 1.5f));
            var hatch = SComp<ST14BunkerLadderComponent>(ladder).Top;
            Assert.That(hatch, Is.Not.Null, "the ladder has no hatch");
            Assert.That(MapOf(hatch!.Value), Is.EqualTo(levels[0]));
            Assert.That(At(hatch.Value), Is.EqualTo(new Vector2(1.5f, 1.5f)));
            upgrades.Go(walker, ladder);
            Assert.That(MapOf(walker), Is.EqualTo(levels[-1]), "the ladder did not take the trooper down");
            SEntMan.DeleteEntity(walker);

            // a machine gun facing north and a tower on the bunkers of the second row
            var nest = SEntMan.SpawnAttachedTo("ST14BunkerMachineGunGarrison", Floor(5.5f, 5.5f), rotation: Direction.North.ToAngle());
            SEntMan.SpawnEntity("ST14BunkerObservation", Floor(8.5f, 5.5f));
            observer = SEntMan.SpawnEntity("MobHuman", Floor(8.5f, 4.5f));
            maxZoomBefore = SComp<Content.Shared.Movement.Components.ContentEyeComponent>(observer).MaxZoom;

            // bugs on the ground north of the bunkers, and one east of the nest
            target = SEntMan.SpawnEntity("ST14MobBugWarrior", Roof(5.5f, 9.5f));
            var larva = SEntMan.SpawnEntity("ST14MobBugLarva", Roof(6.5f, 9.5f));
            var east = SEntMan.SpawnEntity("ST14MobBugWarrior", Roof(10.5f, 6.5f));

            var guns = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14BunkerGarrisonGunSystem>();
            var nestGun = SComp<ST14BunkerGarrisonComponent>(nest).Gun!.Value;
            var nestXform = SEntMan.GetComponent<TransformComponent>(nestGun);
            var nestComp = SComp<ST14BunkerGarrisonGunComponent>(nestGun);
            Assert.That(guns.CanTake((nestGun, nestComp), nestXform, target), "the machine gun does not take a bug in front of it");
            Assert.That(guns.CanTake((nestGun, nestComp), nestXform, east), Is.False, "the machine gun takes a bug off its arc");

            var heavy = SEntMan.SpawnEntity("ST14BunkerAntiLargeGarrisonGun", Roof(5.5f, 6.5f));
            var heavyComp = SComp<ST14BunkerGarrisonGunComponent>(heavy);
            heavyComp.Facing = Direction.North.ToAngle();
            var heavyXform = SEntMan.GetComponent<TransformComponent>(heavy);
            Assert.That(guns.CanTake((heavy, heavyComp), heavyXform, target), "the heavy gun does not take a big bug");
            Assert.That(guns.CanTake((heavy, heavyComp), heavyXform, larva), Is.False, "the heavy gun takes a larva");
            SEntMan.DeleteEntity(heavy);
        });
        await RunTicksSync(90);

        await Server.WaitPost(() =>
        {
            Assert.That(SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>().GetTotalDamage(target).Float(), Is.GreaterThan(0f),
                "the garrisons did not hit the bug in front of them");

            var eye = SComp<Content.Shared.Movement.Components.ContentEyeComponent>(observer);
            Assert.That(eye.MaxZoom.X, Is.GreaterThan(maxZoomBefore.X), "inside under the tower the view is no wider");

            // out of the bunker: the zoom from before
            SEntMan.System<SharedTransformSystem>().SetWorldPosition(observer, new Vector2(2.5f, 2.5f));
        });
        await RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            var eye = SComp<Content.Shared.Movement.Components.ContentEyeComponent>(observer);
            Assert.That(eye.MaxZoom, Is.EqualTo(maxZoomBefore), "out of the bunker the wider view stayed");

            bool Gone(EntityUid uid) => SEntMan.Deleted(uid) || SEntMan.IsQueuedForDeletion(uid);

            // taken down, each takes with it what it put in place
            var gun = SComp<ST14BunkerGarrisonComponent>(garrison).Gun!.Value;
            SEntMan.DeleteEntity(garrison);
            Assert.That(Gone(gun), "the garrison gun stayed after the garrison went");

            var crate = SComp<ST14BunkerUpgradeComponent>(baseUpgrade).InsideEntity!.Value;
            SEntMan.DeleteEntity(baseUpgrade);
            Assert.That(Gone(crate), "the supply crate stayed after the base went");

            var rampComp = SComp<ST14BunkerRampComponent>(ramp);
            var outside = rampComp.Outside!.Value;
            var inside = rampComp.Inside!.Value;
            SEntMan.DeleteEntity(ramp);
            Assert.That(Gone(outside) && Gone(inside), "the ends of the ramp stayed after the ramp went");

            var hatch = SComp<ST14BunkerLadderComponent>(ladder).Top!.Value;
            SEntMan.DeleteEntity(ladder);
            Assert.That(Gone(hatch), "the hatch stayed after the ladder went");
        });
    }

    /// <summary>
    /// A trench segment is 1 to 5 tiles long and 1 or 2 wide, its second row clockwise of the way it is turned
    /// </summary>
    [Test]
    public void TrenchSizeTest()
    {
        var one = ST14TrenchShapes.Shape(ST14DigMode.Trench, new Vector2i(5, 5), Direction.South, 1, 3, 1);
        Assert.That(one, Is.EquivalentTo(new[] { new Vector2i(5, 5) }));

        var five = ST14TrenchShapes.Shape(ST14DigMode.Trench, new Vector2i(5, 5), Direction.South, 5, 3, 1);
        Assert.That(five, Has.Count.EqualTo(5));
        Assert.That(five, Does.Contain(new Vector2i(5, 7)).And.Contain(new Vector2i(5, 3)), "five tiles are not centred");

        // two wide, running east: the second row clockwise of east is south of it
        var wide = ST14TrenchShapes.Shape(ST14DigMode.Trench, new Vector2i(5, 5), Direction.East, 3, 3, 2);
        Assert.That(wide, Has.Count.EqualTo(6));
        Assert.That(wide, Does.Contain(new Vector2i(6, 4)).And.Contain(new Vector2i(4, 5)));
    }

    /// <summary>
    /// A bunker goes flush against the bunker next to it: the cursor near one snaps to the free place beside it, and a
    /// staggered layout touching it is refused. Away from any bunker it goes where the cursor is.
    /// </summary>
    [Test]
    public async Task BunkersSnapFlushTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            (Vector2i Center, int Size)? At(Vector2i t) => trench.DugoutAt(levels[0], t);

            // the first one anywhere
            Assert.That(ST14TrenchShapes.SnappedBunkerStart(new Vector2(3.4f, 3.6f), 3, At), Is.EqualTo(new Vector2i(2, 2)));
            var first = trench.TryLayOut(levels[0], ST14TrenchShapes.BunkerShape(ST14DigMode.Dugout, new Vector2i(1, 1), Direction.South, 3), true, 3f);
            Assert.That(first, Is.Not.Null);
            trench.FinishBlueprint((first!.Value, SComp<ST14TrenchBlueprintComponent>(first.Value)));

            // the cursor east of it, a little off: snaps flush to its east side
            Assert.That(ST14TrenchShapes.SnappedBunkerStart(new Vector2(5.9f, 3.7f), 3, At), Is.EqualTo(new Vector2i(4, 1)),
                "a bunker next to another did not snap flush to it");
            // north of it
            Assert.That(ST14TrenchShapes.SnappedBunkerStart(new Vector2(2.2f, 6.1f), 3, At), Is.EqualTo(new Vector2i(1, 4)));

            // a staggered one touching it is refused, a flush one is laid out
            var staggered = trench.TryLayOut(levels[0], ST14TrenchShapes.BunkerShape(ST14DigMode.Dugout, new Vector2i(4, 2), Direction.South, 3), true, 3f);
            Assert.That(staggered, Is.Null, "a bunker went staggered against another");
            var flush = trench.TryLayOut(levels[0], ST14TrenchShapes.BunkerShape(ST14DigMode.Dugout, new Vector2i(4, 1), Direction.South, 3), true, 3f);
            Assert.That(flush, Is.Not.Null, "a bunker cannot go flush against another");
        });
    }

    /// <summary>
    /// Alt+click gets a trooper down into the trench next to them from the ground, and up onto the ground at its edge
    /// from inside; not onto their own tile, not too far, not into a bunker
    /// </summary>
    [Test]
    public async Task ClimbIntoAndOutOfTrenchTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            for (var x = 3; x <= 7; x++)
            {
                Assert.That(trench.DigTrench(levels[0], new Vector2i(x, 5)));
            }

            var above = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[0], new Vector2(5.5f, 6.5f)));
            Assert.That(trench.TryGetClimb(above, new EntityCoordinates(levels[0], new Vector2(5.4f, 5.5f)), out var down, out var isDown),
                "cannot get down into the trench next to them");
            Assert.That(isDown);
            Assert.That(down.Position, Is.EqualTo(new Vector2(5.5f, 5.5f)));
            Assert.That(trench.TryGetClimb(above, new EntityCoordinates(levels[0], new Vector2(5.5f, 8.5f)), out _, out _), Is.False,
                "got down onto plain ground");
            Assert.That(trench.TryGetClimb(above, new EntityCoordinates(levels[0], new Vector2(8.5f, 5.5f)), out _, out _), Is.False,
                "got down into a trench too far away");

            var inside = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(5.5f, 5.5f)));
            Assert.That(trench.TryGetClimb(inside, new EntityCoordinates(levels[-1], new Vector2(5.5f, 4.4f)), out var up, out var isUp),
                "cannot get up onto the ground at the edge of the trench");
            Assert.That(isUp, Is.False);
            Assert.That(up.Position, Is.EqualTo(new Vector2(5.5f, 4.5f)));
            Assert.That(trench.TryGetClimb(inside, new EntityCoordinates(levels[-1], new Vector2(6.5f, 5.5f)), out _, out _), Is.False,
                "climbed out onto the trench itself");
        });
    }

    /// <summary>
    /// A corner bunker is the staircase half of a 3x3 square, turned by R; it is roofed like a bunker, a square bunker
    /// can be built on to it, and it carries no upgrade
    /// </summary>
    [Test]
    public async Task CornerBunkerTest()
    {
        await Server.WaitAssertion(() =>
        {
            var south = ST14TrenchShapes.Shape(ST14DigMode.Corner, new Vector2(3.5f, 3.5f), Direction.South);
            Assert.That(south, Has.Count.EqualTo(6));
            Assert.That(south, Does.Contain(new Vector2i(2, 2)).And.Contain(new Vector2i(4, 2)).And.Contain(new Vector2i(2, 4)));
            Assert.That(south, Does.Not.Contain(new Vector2i(4, 4)).And.Not.Contain(new Vector2i(3, 4)).And.Not.Contain(new Vector2i(4, 3)));

            var north = ST14TrenchShapes.Shape(ST14DigMode.Corner, new Vector2(3.5f, 3.5f), Direction.North);
            Assert.That(north, Does.Contain(new Vector2i(4, 4)).And.Not.Contain(new Vector2i(2, 2)), "R does not turn the corner bunker");

            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");

            var corner = trench.TryLayOut(levels[0], south, true, 3f);
            Assert.That(corner, Is.Not.Null, "the corner bunker was not laid out");
            trench.FinishBlueprint((corner!.Value, SComp<ST14TrenchBlueprintComponent>(corner.Value)));
            foreach (var tile in south)
            {
                Assert.That(ST14Dugouts.TryGetPart(SEntMan, levels[0], tile, out var part), $"no roof over {tile}");
                Assert.That(part!.Center, Is.EqualTo(new Vector2i(3, 3)));
            }

            Assert.That(ST14Dugouts.TryGetPart(SEntMan, levels[0], new Vector2i(4, 4), out _), Is.False, "the cut corner got a roof");

            // a square bunker built on to its east leg
            var square = ST14TrenchShapes.Shape(ST14DigMode.Dugout, new Vector2(6.5f, 3.5f), Direction.South);
            Assert.That(trench.TryLayOut(levels[0], square, true, 3f), Is.Not.Null, "a bunker cannot be built on to a corner bunker");

            Assert.That(new ST14DugoutUpgradeCondition().Condition(default, new EntityCoordinates(levels[-1], new Vector2(3.5f, 3.5f)), Direction.South),
                Is.False, "an upgrade went on a corner bunker");
        });
    }

    /// <summary>
    /// Trench modifications go on a tile of a trench, one a tile: built from the surface over it, they go down onto its
    /// floor. Filling the trench in takes them away, gives back the ground, brings whoever was down there up, and leaves
    /// no earth walls where no trench runs any more.
    /// </summary>
    [Test]
    public async Task TrenchModsAndFillTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var maps = SEntMan.System<SharedMapSystem>();
            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            for (var x = 3; x <= 5; x++)
            {
                Assert.That(trench.DigTrench(levels[0], new Vector2i(x, 5)));
            }

            var condition = new ST14TrenchModCondition();
            var over = new EntityCoordinates(levels[0], new Vector2(4.5f, 5.5f));
            Assert.That(condition.Condition(default, over, Direction.South), "barbed wire cannot go over a trench");
            Assert.That(condition.Condition(default, new EntityCoordinates(levels[-1], new Vector2(4.5f, 5.5f)), Direction.South),
                "barbed wire cannot go down in a trench");
            Assert.That(condition.Condition(default, new EntityCoordinates(levels[0], new Vector2(4.5f, 8.5f)), Direction.South), Is.False,
                "barbed wire went on the ground");

            var wire = SEntMan.SpawnEntity("ST14TrenchBarbedWire", over);
            var wireXform = SEntMan.GetComponent<TransformComponent>(wire);
            Assert.That(wireXform.MapUid, Is.EqualTo(levels[-1]), "the wire did not go down onto the floor of the trench");
            Assert.That(wireXform.Anchored, "the wire is not fixed to the floor");
            Assert.That(condition.Condition(default, over, Direction.South), Is.False, "a second modification went on the same tile");

            // someone down there when the tile is filled in
            var trooper = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(4.5f, 5.5f)));
            Assert.That(SEntMan.System<Content.Server._ST14.ZLevels.Core.ST14ZLevelsSystem>().TryGetLayerGrid(levels[0], out var surfaceGrid));
            var surface = surfaceGrid!.Value;
            var floor = SComp<MapGridComponent>(levels[-1]);
            bool Ground(Entity<MapGridComponent> grid, int x, int y) =>
                maps.TryGetTileRef(grid, grid.Comp, new Vector2i(x, y), out var tile) && !tile.Tile.IsEmpty;
            int Walls(int x, int y)
            {
                var count = 0;
                var anchored = maps.GetAnchoredEntitiesEnumerator(levels[-1], floor, new Vector2i(x, y));
                while (anchored.MoveNext(out var uid))
                {
                    if (SEntMan.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID == "ST14TrenchWall")
                        count++;
                }

                return count;
            }

            Assert.That(trench.FillTrench(levels[0], new Vector2i(4, 5)), "the middle of the trench was not filled in");
            Assert.That(Ground(surface, 4, 5), "the ground did not come back");
            Assert.That(SEntMan.Deleted(wire) || SEntMan.IsQueuedForDeletion(wire), "the wire stayed in the filled trench");
            Assert.That(SEntMan.GetComponent<TransformComponent>(trooper).MapUid, Is.EqualTo(levels[0]), "the trooper stayed under the ground");
            Assert.That(Walls(4, 5), Is.EqualTo(1), "no earth wall between the two ends of the trench");

            Assert.That(trench.FillTrench(levels[0], new Vector2i(4, 5)), Is.False, "the same tile was filled in twice");
            Assert.That(trench.FillTrench(levels[0], new Vector2i(3, 5)));
            Assert.That(trench.FillTrench(levels[0], new Vector2i(5, 5)));

            // nothing left down there: solid earth, no walls
            for (var x = 2; x <= 6; x++)
            for (var y = 4; y <= 6; y++)
            {
                Assert.That(Ground(surface, x, y), $"the ground at {x},{y} did not come back");
                Assert.That(Walls(x, y), Is.Zero, $"an earth wall stayed at {x},{y}");
                Assert.That(Ground((levels[-1], floor), x, y), Is.False, $"the floor of the trench stayed at {x},{y}");
            }
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

    /// <summary>
    /// A bullet fired in an open trench flies on the level above; under a bunker roof it stays down
    /// </summary>
    [Test]
    public async Task ShootOutOfTrenchTest()
    {
        await Server.WaitAssertion(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var shots = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchShotSystem>();
            var physics = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();

            var levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            for (var x = 5; x <= 9; x++)
            {
                trench.DigTrench(levels[0], new Vector2i(x, 5));
            }

            SEntMan.SpawnEntity("ST14BunkerRoofFrame", new EntityCoordinates(levels[-1], new Vector2(8.5f, 5.5f)));

            var open = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[-1], new Vector2(5.5f, 5.5f)));
            physics.SetLinearVelocity(open, new Vector2(0f, 20f));
            Assert.That(shots.TryLiftShot(open), "a shot from an open trench did not leave it");
            Assert.That(SEntMan.GetComponent<TransformComponent>(open).MapUid, Is.EqualTo(levels[0]));
            Assert.That(physics.GetMapLinearVelocity(open).Y, Is.EqualTo(20f).Within(0.01f), "the shot lost its speed");

            var covered = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[-1], new Vector2(8.5f, 5.5f)));
            Assert.That(shots.TryLiftShot(covered), Is.False, "a shot went through the bunker roof");
            Assert.That(SEntMan.GetComponent<TransformComponent>(covered).MapUid, Is.EqualTo(levels[-1]));
        });
    }

    /// <summary>
    /// Fire aimed at a soldier in an open trench drops into it at the aimed spot and hits them; fire aimed past the
    /// trench flies over them
    /// </summary>
    [Test]
    public async Task ShootIntoTrenchTest()
    {
        Dictionary<int, EntityUid> levels = default!;
        EntityUid aimed = default;
        EntityUid past = default;
        EntityUid beside = default;
        var shots = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchShotSystem>();
        var damage = SEntMan.System<Content.Shared.Damage.Systems.DamageableSystem>();

        float Piercing(EntityUid uid) => damage.GetAllDamage(uid).DamageDict.GetValueOrDefault("Piercing").Float();

        await Server.WaitPost(() =>
        {
            var layers = SEntMan.System<ST14ZLayerSystem>();
            var trench = SEntMan.System<Content.Server._ST14.ZLevels.Fortifications.ST14TrenchSystem>();
            var physics = SEntMan.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            levels = layers.EnsureLayers(MakeSector(out _), "ST14PlanetSector");
            trench.DigTrench(levels[0], new Vector2i(8, 3));
            trench.DigTrench(levels[0], new Vector2i(8, 7));
            trench.DigTrench(levels[0], new Vector2i(8, 9));
            trench.DigTrench(levels[0], new Vector2i(9, 9));
            trench.DigTrench(levels[0], new Vector2i(10, 9));
            aimed = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(8.5f, 3.5f)));
            past = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(8.5f, 7.5f)));
            beside = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(levels[-1], new Vector2(10.5f, 9.5f)));

            // one bullet aimed at the soldier in the trench, one over the other soldier at the wall behind him
            var first = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[0], new Vector2(3.5f, 3.5f)));
            physics.SetLinearVelocity(first, new Vector2(15f, 0f));
            Assert.That(shots.TryLiftShot(first), Is.False, "a bullet fired on the surface was lifted into the sky");
            Assert.That(shots.AimIntoTrench(first, new Vector2(8.5f, 3.5f)), "aiming at an open trench did not count");

            var second = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[0], new Vector2(3.5f, 7.5f)));
            physics.SetLinearVelocity(second, new Vector2(15f, 0f));
            Assert.That(shots.AimIntoTrench(second, new Vector2(10.5f, 7.5f)), Is.False, "aiming at the ground went into a trench");

            // aimed into the trench but not at the soldier two tiles along it: lost in the floor
            var third = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[0], new Vector2(3.5f, 9.5f)));
            physics.SetLinearVelocity(third, new Vector2(15f, 0f));
            Assert.That(shots.AimIntoTrench(third, new Vector2(8.5f, 9.5f)));

            // fired in the trench along it the bullet stays down; aimed at the ground it goes over the edge
            var along = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[-1], new Vector2(8.5f, 9.5f)));
            Assert.That(shots.TryLiftShot(along, new Vector2(10.5f, 9.5f)), Is.False, "a bullet fired along the trench was lifted out of it");
            var over = SEntMan.SpawnEntity("BulletLightRifle", new EntityCoordinates(levels[-1], new Vector2(9.5f, 9.5f)));
            Assert.That(shots.TryLiftShot(over, new Vector2(9.5f, 6.5f)), "a bullet fired at the ground stayed in the trench");
            SEntMan.DeleteEntity(along);
            SEntMan.DeleteEntity(over);
        });

        await RunTicksSync(60);

        await Server.WaitAssertion(() =>
        {
            // only bullets pierce: the soldiers also choke on the airless planet
            Assert.That(Piercing(aimed), Is.GreaterThan(0f), "the bullet aimed into the trench missed the soldier");
            Assert.That(Piercing(past), Is.EqualTo(0f), "a bullet aimed past the trench came down into it");
            Assert.That(Piercing(beside), Is.EqualTo(0f), "a bullet aimed at an empty spot of the trench hit the soldier beside it");
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
