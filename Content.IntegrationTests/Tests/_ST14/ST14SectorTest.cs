using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.MainHub;
using Content.Server._ST14.Sectors;
using Content.Server.GameTicking;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sandstorm;
using Content.Shared._ST14.Sectors;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Spawners.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14SectorSystem))]
public sealed class ST14SectorTest : GameTest
{
    // a plain string: on a server without the hive this prototype does not exist
    private static readonly string BugSpawnPoint = "ST14SpawnPointBug";

    /// <summary>
    /// Every terrain of the galaxy map has a sector map file and the war has a next battle
    /// </summary>
    [Test]
    public async Task TerrainsHaveMapsTest()
    {
        var server = Pair.Server;
        var protoMan = server.ProtoMan;
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var terrains = protoMan.EnumeratePrototypes<ST14SectorTerrainPrototype>().ToList();
            for (var i = 0; i < ST14PlanetWar.TerrainKinds; i++)
            {
                Assert.That(terrains.Any(t => t.Index == i && t.Maps.Count > 0), $"Terrain {i} has no sector map.");
            }

            foreach (var file in terrains.SelectMany(t => t.Maps))
            {
                Assert.That(resources.ContentFileExists(file), $"The sector map {file} does not exist.");
            }

            Assert.That(protoMan.EnumeratePrototypes<GameMapPrototype>().Any(map => map.ID == "ST14SectorWar"), "The sector rounds have no game map.");

            server.CfgMan.SetCVar(ST14CVars.SectorRounds, true);
            var forced = server.CfgMan.GetCVar(CCVars.GameMap);
            server.CfgMan.SetCVar(CCVars.GameMap, string.Empty);

            var next = server.System<ST14SectorSystem>().NextBattle(out _, out var cell);

            server.CfgMan.SetCVar(CCVars.GameMap, forced);
            server.CfgMan.SetCVar(ST14CVars.SectorRounds, false);

            Assert.That(next, "The war has no next battle.");
            Assert.That(cell, Is.GreaterThanOrEqualTo(0), "The war has no target sector.");
        });
    }

    /// <summary>
    /// The sector template is a hexagon closed off by the storm with a passage on every edge, spawn points for both sides and places for queens
    /// </summary>
    [Test]
    public async Task TemplateLayoutTest()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadMap(new ResPath("/Maps/_ST14/Sectors/template.yml"), out var map, out _),
                "The sector template does not load.");

            var mapId = entMan.GetComponent<TransformComponent>(map!.Value).MapID;
            var storm = entMan.EntityQuery<ST14SandstormWallComponent, TransformComponent>(true).Count(x => x.Item2.MapID == mapId);
            var points = entMan.EntityQuery<SpawnPointComponent, TransformComponent>(true).Where(x => x.Item2.MapID == mapId).Select(x => x.Item1.Job?.Id).ToList();
            var queens = entMan.EntityQuery<ST14SectorQueenSlotComponent, TransformComponent>(true).Count(x => x.Item2.MapID == mapId);
            var gates = entMan.EntityQuery<ST14SectorGateComponent, TransformComponent>(true).Where(x => x.Item2.MapID == mapId).Select(x => x.Item1.Edge).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(storm, Is.GreaterThan(1000), "The storm does not run along the edges.");
                Assert.That(gates.OrderBy(e => e), Is.EqualTo(Enumerable.Range(0, 6)), "The template needs one passage on each of the six edges.");
                Assert.That(points, Does.Contain("ST14Engineer"), "No landing points for the troopers.");
                // the hive's points come with the hive
                if (server.ProtoMan.HasIndex<EntityPrototype>(BugSpawnPoint))
                {
                    Assert.That(points, Does.Contain("ST14Bug"), "No spawn points for the bugs.");
                    Assert.That(points, Does.Contain("ST14BrainBug"), "No spawn point for the brain bug.");
                }
                Assert.That(queens, Is.GreaterThanOrEqualTo(2), "Too few queen places.");
            });

            entMan.DeleteEntity(map.Value);
        });
    }

    /// <summary>
    /// A war over two rounds: the round loads the target, what stands on its map is saved when the round ends and comes
    /// back next round, the corpse does not, the next sector loads beside it, a passage leads there, and resetting the war wipes the saves
    /// </summary>
    [Test]
    public async Task RoundsKeepSectorsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true, DummyTicker = false, Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var sectors = server.System<ST14SectorSystem>();
        var war = server.System<ST14GalaxyWarSystem>();
        var gates = server.System<ST14SectorGateSystem>();
        var resources = server.ResolveDependency<IResourceManager>();

        var planet = string.Empty;
        var first = -1;
        var builtAt = new Vector2(100.5f, 100.5f);
        var standsAt = new Vector2(90.5f, 95.5f);
        string firstJob = null;

        // the test player has no job preferences and may draw another job next round, then the body rightly stays behind
        string JobOf(EntityUid body)
        {
            var mind = server.System<Content.Shared.Mind.SharedMindSystem>();
            if (!mind.TryGetMind(body, out var mindId, out _))
                return null;

            server.System<Content.Shared.Roles.Jobs.SharedJobSystem>().MindTryGetJobId(mindId, out var job);
            return job?.Id;
        }

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(ST14CVars.SectorRounds, true);
            server.CfgMan.SetCVar(CCVars.GameMap, string.Empty);
            planet = war.CurrentPlanet()!;
            Assert.That(war.Reset(planet));
            ticker.RestartRound();
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            Assert.That(war.TryGetWar(planet, out var state, out first));
            Assert.That(state.RegionCenters, Does.Contain(first), "The war does not start from the centre of a region.");
            Assert.That(sectors.LoadedMaps.Keys, Is.EquivalentTo(new[] { first }), "The first round should load the target alone.");

            var map = sectors.LoadedMaps[first];
            var grid = entMan.GetComponent<TransformComponent>(FindGate(entMan, map, 0)).GridUid!.Value;
            entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, builtAt));
            var corpse = entMan.SpawnEntity("MobHuman", new EntityCoordinates(grid, builtAt + new Vector2(2, 0)));
            server.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(corpse, Content.Shared.Mobs.MobState.Dead);

            // the player started in the sector of the battle and leaves their body at a place of their own
            Assert.That(pair.Player!.AttachedEntity, Is.Not.Null, "The player did not spawn.");
            var body = pair.Player.AttachedEntity!.Value;
            firstJob = JobOf(body);
            Assert.That(entMan.GetComponent<TransformComponent>(body).MapUid, Is.EqualTo(map), "The player did not spawn in the sector of the battle.");
            server.System<SharedTransformSystem>().SetCoordinates(body, new EntityCoordinates(grid, standsAt));

            Assert.That(war.ApplyResult(planet, ST14GalaxyFaction.Federation));
            Assert.That(state.Cells[first].Faction, Is.EqualTo(ST14GalaxyFaction.Federation));
            Assert.That(state.Turn, Is.EqualTo(ST14GalaxyFaction.Arachnid), "The turn did not pass to the bugs.");
            Assert.That(war.TryGetWar(planet, out _, out var next));
            Assert.That(next, Is.EqualTo(first), "The bugs can only strike back at the one sector the Federation holds.");

            ticker.EndRound();
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(war.TryGetWar(planet, out var state, out _));
            Assert.That(state.Cells[first].Initialized, "The sector was not marked as saved.");
            Assert.That(resources.UserData.Exists(new ResPath($"/st14_sectors/{planet}_{first}.yml")), "The sector has no save.");
            ticker.RestartRound();
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
            var staging = war.StagingOf(planet);
            Assert.That(staging, Is.GreaterThanOrEqualTo(0), "The bugs have no sector to gather in.");
            Assert.That(sectors.LoadedMaps.Keys, Is.EquivalentTo(new[] { first, staging }), "The saved sector and the staging sector should both load.");
            Assert.That(sectors.StartOf(ST14GalaxyFaction.Arachnid), Is.EqualTo(staging), "The bugs must start in their staging sector.");
            Assert.That(sectors.StartOf(ST14GalaxyFaction.Federation), Is.EqualTo(first), "The troopers must start in the sector they hold.");

            var map = sectors.LoadedMaps[first];
            var mapId = entMan.GetComponent<TransformComponent>(map).MapID;
            var walls = entMan.AllEntityQueryEnumerator<Robust.Shared.Physics.Components.PhysicsComponent, MetaDataComponent, TransformComponent>();
            var wall = false;
            while (walls.MoveNext(out _, out var meta, out var xform))
            {
                wall |= xform.MapID == mapId && meta.EntityPrototype?.ID == "WallSolid" && (xform.LocalPosition - builtAt).Length() < 0.1f;
            }

            Assert.That(wall, "The wall built last round did not come back.");

            // the player is back in the same body, where it was left
            var player = pair.Player!.AttachedEntity;
            Assert.That(player, Is.Not.Null, "The player did not come back.");
            if (JobOf(player!.Value) == firstJob)
            {
                Assert.That(entMan.TryGetComponent<ST14SectorOwnerComponent>(player, out var owner) && owner.UserId == pair.Player.UserId.UserId.ToString(), "The player got a new body.");
                var at = entMan.GetComponent<TransformComponent>(player.Value);
                Assert.That(at.MapID, Is.EqualTo(mapId), "The body came back in another sector.");
                Assert.That((at.LocalPosition - standsAt).Length(), Is.LessThan(0.1f), "The body came back in another place.");
            }

            var mobs = entMan.AllEntityQueryEnumerator<Content.Shared.Mobs.Components.MobStateComponent, TransformComponent>();
            while (mobs.MoveNext(out var uid, out _, out var xform))
            {
                Assert.That(xform.MapID != mapId || !server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsDead(uid) || entMan.GetComponent<MetaDataComponent>(uid).EntityLifeStage >= EntityLifeStage.Terminating,
                    "A corpse came back with the save.");
            }

            Assert.That(war.TryGetWar(planet, out var state, out _));
            var edge = Array.IndexOf(state.Grid.Edges[first], staging);
            var gate = FindGate(entMan, map, edge);
            var destination = gates.Destination((gate, entMan.GetComponent<ST14SectorGateComponent>(gate)), out _);
            Assert.That(destination, Is.Not.Null, "The passage into the loaded neighbour leads nowhere.");
            Assert.That(entMan.GetComponent<TransformComponent>(destination!.Value.EntityId).MapUid, Is.EqualTo(sectors.LoadedMaps[staging]), "The passage leads to the wrong sector.");

            var closed = state.Grid.Edges[first].First(n => !sectors.LoadedMaps.ContainsKey(n));
            var closedGate = FindGate(entMan, map, Array.IndexOf(state.Grid.Edges[first], closed));
            Assert.That(gates.Destination((closedGate, entMan.GetComponent<ST14SectorGateComponent>(closedGate)), out var message), Is.Null, "A passage leads into a sector nobody fought for.");
            Assert.That(message, Is.Not.Null);

            war.Reset(planet);
            Assert.That(resources.UserData.Exists(new ResPath($"/st14_sectors/{planet}_{first}.yml")), Is.False, "Resetting the war left the save.");

            server.CfgMan.SetCVar(ST14CVars.SectorRounds, false);
            server.CfgMan.SetCVar(CCVars.GameMap, "Empty");
            ticker.RestartRound();
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    private static EntityUid FindGate(IEntityManager entMan, EntityUid map, int edge)
    {
        var mapId = entMan.GetComponent<TransformComponent>(map).MapID;
        var query = entMan.AllEntityQueryEnumerator<ST14SectorGateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var gate, out var xform))
        {
            if (gate.Edge == edge && xform.MapID == mapId)
                return uid;
        }

        Assert.Fail($"The map has no passage on edge {edge}.");
        return default;
    }
}
