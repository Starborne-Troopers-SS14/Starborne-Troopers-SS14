#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Conditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

/// <summary>
/// ST14: the engineer's build menu. Every buildable sits in a tab that exists; stairs and gates check the extra tiles
/// they take, turned the way they are built facing.
/// </summary>
public sealed class ST14BuildMenuTest : GameTest
{
    private static readonly ProtoId<ST14EngineerBuildablePrototype>[] HellBuildables = { "HellWall", "HellStairs", "HellGate" };

    [Test]
    public async Task EveryBuildableHasATabTest()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var buildable in prototypes.EnumeratePrototypes<ST14EngineerBuildablePrototype>())
            {
                Assert.That(prototypes.HasIndex(buildable.Category), $"{buildable.ID} is in no tab of the build menu");
            }

            foreach (var id in HellBuildables)
            {
                Assert.That(prototypes.HasIndex(id), $"{id} is not in the build menu");
            }
        });
    }

    /// <summary>
    /// The light sand shader of the weather parses with the engine's shader parser (internal, so through reflection;
    /// the GPU compile itself only happens in a real client), with the uniforms the weather overlay sets
    /// </summary>
    [Test]
    public async Task LightSandShaderParsesTest()
    {
        await Client.WaitAssertion(() =>
        {
            var resources = Client.ResolveDependency<Robust.Shared.ContentPack.IResourceManager>();
            var code = resources.ContentFileReadAllText(new Robust.Shared.Utility.ResPath("/Textures/_ST14/Shaders/light_sand.swsl"));
            var parser = typeof(Robust.Client.Graphics.ShaderPrototype).Assembly.GetType("Robust.Client.Graphics.ShaderParser")!;
            var parse = parser.GetMethod("Parse", new[] { typeof(string), typeof(Robust.Shared.ContentPack.IResourceManager) })!;
            var parsed = parse.Invoke(null, new object[] { code, resources })!;

            var uniforms = (System.Collections.IDictionary) parsed.GetType().GetProperty("Uniforms")!.GetValue(parsed)!;
            foreach (var name in new[] { "viewMin", "viewSize", "windDir", "speed", "strength", "sandColor", "brightness" })
            {
                Assert.That(uniforms.Contains(name), $"the light sand shader has no uniform {name}");
            }
        });
    }

    [Test]
    public async Task TilesFreeTurnsWithTheBuildingTest()
    {
        await Server.WaitAssertion(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            var floor = Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId;
            var map = maps.CreateMap(out var mapId);
            var grid = maps.CreateGridEntity(mapId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 9; x++)
            for (var y = 0; y < 9; y++)
                tiles.Add((new Vector2i(x, y), new Tile(floor)));
            maps.SetTiles(grid, tiles);

            // a wall two tiles south of the middle
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 4.5f, 2.5f));

            var stairs = new ST14TilesFreeCondition { Offsets = new() { new Vector2i(0, -1), new Vector2i(0, -2), new Vector2i(0, -3) } };
            var at = new EntityCoordinates(grid, 4.5f, 4.5f);
            Assert.That(stairs.Condition(default, at, Direction.South), Is.False, "stairs went down into a wall");
            Assert.That(stairs.Condition(default, at, Direction.North), "stairs facing north cannot go where north is free");
            Assert.That(stairs.Condition(default, at, Direction.East), "stairs facing east cannot go where east is free");

            // facing east the steps run east: the wall there stops them
            SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 6.5f, 4.5f));
            Assert.That(stairs.Condition(default, at, Direction.East), Is.False, "stairs facing east went down into a wall");

            // the gate's second half is clockwise of it: west when it faces south
            var gate = new ST14TilesFreeCondition { Offsets = new() { new Vector2i(-1, 0) } };
            Assert.That(gate.Condition(default, new EntityCoordinates(grid, 7.5f, 4.5f), Direction.South), Is.False,
                "a gate facing south went half into the wall west of it");
            Assert.That(gate.Condition(default, new EntityCoordinates(grid, 6.5f, 6.5f), Direction.South));

            // the map goes, or the next test on this pair runs into it
            SEntMan.DeleteEntity(map);
        });
    }
}
