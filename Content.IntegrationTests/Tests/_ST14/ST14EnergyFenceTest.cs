using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.EnergyFence;
using Content.Server.Power.Components;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Power.EntitySystems;
using Content.Shared.SubFloor;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14EnergyFenceSystem))]
public sealed class ST14EnergyFenceTest : GameTest
{
    /// <summary>
    /// Pylons in range pair up, nearest first, at most two links each; pylons farther than nine tiles do not
    /// </summary>
    [Test]
    public async Task LinkTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var fence = entMan.System<ST14EnergyFenceSystem>();

        EntityUid a = default, b = default, c = default, far = default;
        EntityUid hub = default, n1 = default, n2 = default, n3 = default;
        await server.WaitPost(() =>
        {
            FillTiles(entMan, map, 0, 0, 40, 30);

            // A row: a - b (4 tiles) - c (8 tiles), far is 10 tiles past c
            a = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            b = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            c = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 12.5f, 0.5f));
            far = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 22.5f, 0.5f));

            // A hub with three neighbours far enough apart that they cannot link each other within range
            hub = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 20.5f, 20.5f));
            n1 = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 14.5f, 20.5f));
            n2 = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 27.5f, 20.5f));
            n3 = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 20.5f, 28.5f));
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(fence.AreLinked(a, b), "Pylons 4 tiles apart did not link.");
            Assert.That(fence.AreLinked(b, c), "Pylons 8 tiles apart did not link.");
            Assert.That(fence.AreLinked(c, far), Is.False, "Pylons 10 tiles apart linked.");
            Assert.That(fence.AreLinked(a, c), Is.False, "Pylons 12 tiles apart linked.");

            Assert.That(entMan.GetComponent<ST14EnergyPylonComponent>(hub).LinkCount, Is.EqualTo(2), "The hub pylon does not have exactly two links.");
            Assert.That(fence.AreLinked(hub, n1), "The nearest neighbour of the hub did not link.");
            Assert.That(fence.AreLinked(hub, n2), "The second nearest neighbour of the hub did not link.");
            Assert.That(fence.AreLinked(hub, n3), Is.False, "The hub took a third link.");

            entMan.DeleteEntity(n1);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(fence.AreLinked(hub, n3), "The freed link of the hub was not taken by the next pylon.");
        });
    }

    /// <summary>
    /// Generator → MV cable → energy box with a block → LV cable → two pylons. Idle there is no arc;
    /// a bug on the line is wrapped and shocked at once, a trooper is not. Breaker off or no block: the fence goes dark.
    /// Generator off: the fence lives on the block
    /// </summary>
    [Test]
    public async Task PowerAndZapTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var fence = entMan.System<ST14EnergyFenceSystem>();
        var boxes = entMan.System<ST14EnergyBoxSystem>();
        var generators = entMan.System<ST14FieldGeneratorSystem>();
        var battery = entMan.System<SharedBatterySystem>();
        var damageable = entMan.System<DamageableSystem>();

        EntityUid pylonA = default, pylonB = default, box = default, generator = default;
        await server.WaitPost(() =>
        {
            FillTiles(entMan, map, 0, 0, 10, 8);

            generator = entMan.SpawnEntity("ST14FieldGenerator", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            for (var x = 0; x <= 2; x++)
            {
                entMan.SpawnEntity("CableMV", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }

            box = entMan.SpawnEntity("ST14EnergyBox", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
            entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(map.Grid, 2.5f, 1.5f));
            entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(map.Grid, 2.5f, 2.5f));
            for (var x = 0; x <= 7; x++)
            {
                entMan.SpawnEntity("CableApcExtension", new EntityCoordinates(map.Grid, x + 0.5f, 3.5f));
            }

            pylonA = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 0.5f, 3.5f));
            pylonB = entMan.SpawnEntity("ST14EnergyPylon", new EntityCoordinates(map.Grid, 7.5f, 3.5f));
        });

        // The box is built empty and closed: open the door, put the block in
        await server.WaitPost(() =>
        {
            var boxEnt = (box, entMan.GetComponent<ST14EnergyBoxComponent>(box));
            Assert.That(boxes.GetBlock(boxEnt), Is.Null, "The energy box was built with a block in it.");
            Assert.That(boxes.TryToggleDoor(boxEnt, null), "The energy box door did not open.");
        });
        await pair.RunSeconds(1f);
        await server.WaitPost(() =>
        {
            var block = entMan.SpawnEntity("ST14EnergyBlock", entMan.GetComponent<TransformComponent>(box).Coordinates);
            Assert.That(entMan.System<ItemSlotsSystem>().TryInsert((box, null), "energy_block", block, null),
                "The energy block did not go into the open box.");
        });

        // Well past the power-up delay
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ST14EnergyPylonComponent>(pylonA).Powered, "Pylon A got no power through the energy box.");
            Assert.That(entMan.GetComponent<ST14EnergyPylonComponent>(pylonB).Powered, "Pylon B got no power through the energy box.");
            Assert.That(fence.AreLinked(pylonA, pylonB), "The pylons did not link.");
            Assert.That(fence.IsLinkActive(pylonA, pylonB), Is.False, "The arc burns with no bug around.");
        });

        EntityUid bug = default, trooper = default;
        await server.WaitPost(() =>
        {
            bug = entMan.SpawnEntity("ST14MobBugWarrior", new EntityCoordinates(map.Grid, 3.5f, 3.5f));
            trooper = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 5.5f, 3.5f));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(fence.IsLinkActive(pylonA, pylonB), "A bug on the line did not light the arc.");
            Assert.That(Hurt(entMan, damageable, bug), "The bug was not hit at once.");
            Assert.That(entMan.TryGetComponent<ST14FenceShockedComponent>(bug, out var shocked) && shocked.Depth > 0.9f,
                "The bug on the line is not marked as deep in the zone.");
            Assert.That(Hurt(entMan, damageable, trooper), Is.False, "The fence hurt a trooper.");

            var links = entMan.GetComponent<ST14EnergyPylonComponent>(pylonA).Links;
            Assert.That(links, Has.Count.EqualTo(1), "The link is not in the networked state of its pylon.");
            Assert.That(links[0].Wrapped, Has.Length.EqualTo(1), "The wrapped bug is not in the networked state.");

            entMan.DeleteEntity(bug);
            entMan.DeleteEntity(trooper);
        });

        // Breaker off: dark
        await server.WaitPost(() => boxes.SetBreaker((box, entMan.GetComponent<ST14EnergyBoxComponent>(box)), false));
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ST14EnergyPylonComponent>(pylonA).Powered, Is.False, "The pylons stayed live with the breaker off.");
        });

        // Breaker on, generator off: the block carries the fence and loses charge
        float startCharge = 0;
        await server.WaitPost(() =>
        {
            boxes.SetBreaker((box, entMan.GetComponent<ST14EnergyBoxComponent>(box)), true);
            generators.SetOn((generator, entMan.GetComponent<ST14FieldGeneratorComponent>(generator)), false);
            startCharge = entMan.GetComponent<ST14EnergyBoxComponent>(box).Charge;
        });
        await pair.RunSeconds(5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<PowerSupplierComponent>(generator).Enabled, Is.False, "The generator did not switch off.");
            Assert.That(entMan.GetComponent<ST14EnergyPylonComponent>(pylonA).Powered, "The pylons did not run on the energy block.");
            Assert.That(entMan.GetComponent<ST14EnergyBoxComponent>(box).Charge, Is.LessThan(startCharge), "The energy block did not drain.");

            // No block: no current at all
            var block = boxes.GetBlock((box, entMan.GetComponent<ST14EnergyBoxComponent>(box)));
            Assert.That(block, Is.Not.Null, "The energy block fell out of the box.");
            Assert.That(battery.GetCharge(block!.Value).Charge, Is.LessThan(battery.GetCharge(block.Value).MaxCharge),
                "The energy box did not write the drained charge back into the block.");
            entMan.DeleteEntity(block);
        });

        await server.WaitPost(() => generators.SetOn((generator, entMan.GetComponent<ST14FieldGeneratorComponent>(generator)), true));
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ST14EnergyPylonComponent>(pylonA).Powered, Is.False, "The energy box passed current without a block.");
        });
    }

    /// <summary>
    /// Sand covers cables, a dug trench does not
    /// </summary>
    [Test]
    public async Task TrenchTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var mapSystem = entMan.System<SharedMapSystem>();
        var subFloor = entMan.System<SharedSubFloorHideSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Comp;
            mapSystem.SetTile(map.Grid, new Vector2i(0, 0), new Tile(tileDefs["ST14FloorDunes"].TileId));
            mapSystem.SetTile(map.Grid, new Vector2i(1, 0), new Tile(tileDefs["ST14FloorDunesTrench"].TileId));

            Assert.That(subFloor.HasFloorCover(map.Grid, grid, new Vector2i(0, 0)), "Dunes do not hide cables.");
            Assert.That(subFloor.HasFloorCover(map.Grid, grid, new Vector2i(1, 0)), Is.False, "A trench hides its cable.");
        });
    }

    /// <summary>
    /// Shock damage only: the test map has an atmosphere, and a mob on it may take pressure or suffocation damage
    /// on any tick, which has nothing to do with the fence
    /// </summary>
    private static bool Hurt(IEntityManager entMan, DamageableSystem damageable, EntityUid uid)
    {
        var damage = damageable.GetAllDamage(uid).DamageDict;
        return damage.TryGetValue("Shock", out var shock) && shock > FixedPoint2.Zero;
    }

    private static void FillTiles(IEntityManager entMan, TestMapData map, int x0, int y0, int x1, int y1)
    {
        var mapSystem = entMan.System<SharedMapSystem>();
        for (var x = x0; x <= x1; x++)
        {
            for (var y = y0; y <= y1; y++)
            {
                mapSystem.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
            }
        }
    }
}
