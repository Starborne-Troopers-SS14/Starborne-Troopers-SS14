using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.EnergyFence;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14EnergyFenceSystem))]
public sealed class ST14EnergyFenceTest : GameTest
{
    /// <summary>
    /// Три пилона уголком и щиток рядом: между ними встают лучи по X и по Y.
    /// Без энергоблока в щитке лучи гаснут
    /// </summary>
    [Test]
    public async Task LinkTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var fence = entMan.System<ST14EnergyFenceSystem>();

        EntityUid box = default;
        await server.WaitPost(() =>
        {
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    mapSystem.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 5.5f, 1.5f));
            entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 0.5f, 5.5f));
            box = entMan.SpawnEntity("ST14EnergyBox", new EntityCoordinates(map.Grid, 3.5f, 3.5f));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            CountBeams(entMan, out var horizontal, out var vertical);
            Assert.That(horizontal, Is.EqualTo(4), "Wrong number of arc tiles between the pylons along X.");
            Assert.That(vertical, Is.EqualTo(3), "Wrong number of arc tiles between the pylons along Y.");

            Assert.That(fence.GetBlock((box, entMan.GetComponent<ST14EnergyBoxComponent>(box))), Is.Not.Null,
                "The energy box was built without an energy block.");
            entMan.DeleteEntity(fence.GetBlock((box, entMan.GetComponent<ST14EnergyBoxComponent>(box))));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            CountBeams(entMan, out var horizontal, out var vertical);
            Assert.That(horizontal + vertical, Is.Zero, "The arcs stayed after the energy block was pulled out.");
        });
    }

    /// <summary>
    /// Жук, вставший в луч, получает удар током и отлетает; заряд энергоблока уменьшается
    /// </summary>
    [Test]
    public async Task ZapTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSystem = entMan.System<SharedMapSystem>();
        var fence = entMan.System<ST14EnergyFenceSystem>();
        var battery = entMan.System<SharedBatterySystem>();
        var damageable = entMan.System<DamageableSystem>();

        EntityUid box = default;
        await server.WaitPost(() =>
        {
            for (var x = 0; x < 8; x++)
            {
                for (var y = 0; y < 8; y++)
                {
                    mapSystem.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }
            }

            entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 0.5f, 3.5f));
            entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 6.5f, 3.5f));
            box = entMan.SpawnEntity("ST14EnergyBox", new EntityCoordinates(map.Grid, 3.5f, 6.5f));
        });

        await server.WaitRunTicks(5);

        EntityUid bug = default;
        await server.WaitPost(() =>
        {
            bug = entMan.SpawnEntity("ST14MobBugWarrior", new EntityCoordinates(map.Grid, 3.5f, 3.5f));
        });

        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(damageable.GetTotalDamage(bug), Is.GreaterThan(FixedPoint2.Zero), "The arc did not hurt the bug.");
            var block = fence.GetBlock((box, entMan.GetComponent<ST14EnergyBoxComponent>(box)));
            Assert.That(battery.GetCharge(block!.Value).Charge, Is.LessThan(1000f), "The hit did not take charge from the energy block.");
        });
    }

    private static void CountBeams(IEntityManager entMan, out int horizontal, out int vertical)
    {
        horizontal = 0;
        vertical = 0;
        var query = entMan.EntityQueryEnumerator<ST14EnergyBeamComponent>();
        while (query.MoveNext(out _, out var beam))
        {
            if (beam.Vertical)
                vertical++;
            else
                horizontal++;
        }
    }
}
