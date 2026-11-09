using Content.IntegrationTests.Fixtures;
using Content.Shared._ST14.Sandstorm;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14SandstormWallComponent))]
public sealed class ST14SandstormTest : GameTest
{
    /// <summary>
    /// A 5x5 block of storm: the outer ring touches open ground and is the soft fringe,
    /// the 3x3 inside is solid wall
    /// </summary>
    [Test]
    public async Task FringeTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var fixtures = entMan.System<FixtureSystem>();

        var walls = new EntityUid[5, 5];
        await server.WaitPost(() =>
        {
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    mapSystem.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    walls[x, y] = entMan.SpawnEntity("ST14SandstormWall", new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f));
                }
            }
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    var edge = x == 0 || y == 0 || x == 4 || y == 4;
                    var wall = walls[x, y];
                    Assert.That(entMan.GetComponent<ST14SandstormWallComponent>(wall).Fringe, Is.EqualTo(edge),
                        $"Storm tile {x},{y} has the wrong depth.");
                    Assert.That(fixtures.GetFixtureOrNull(wall, "fix1")!.Hard, Is.EqualTo(!edge),
                        $"Storm tile {x},{y} has the wrong collision.");
                }
            }
        });
    }
}
