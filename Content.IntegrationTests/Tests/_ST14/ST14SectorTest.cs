using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.Sectors;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.Sandstorm;
using Content.Shared._ST14.Sectors;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Spawners.Components;
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
    /// <summary>
    /// Every terrain of the galaxy map has a sector map, and with sector rounds on the war's target picks one
    /// </summary>
    [Test]
    public async Task TargetSectorHasMapTest()
    {
        var server = Pair.Server;
        var protoMan = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var terrains = protoMan.EnumeratePrototypes<ST14SectorTerrainPrototype>().ToList();
            for (var i = 0; i < Content.Shared._ST14.MainHub.ST14PlanetWar.TerrainKinds; i++)
            {
                Assert.That(terrains.Any(t => t.Index == i && t.Maps.Count > 0), $"Terrain {i} has no sector map.");
            }

            // the ids are plain strings, the client knows no game maps
            foreach (var terrain in terrains)
            {
                foreach (var id in terrain.Maps)
                {
                    Assert.That(protoMan.HasIndex<GameMapPrototype>(id), $"Terrain {terrain.ID} names a game map {id} that does not exist.");
                }
            }

            server.CfgMan.SetCVar(ST14CVars.SectorRounds, true);
            var forced = server.CfgMan.GetCVar(CCVars.GameMap);
            server.CfgMan.SetCVar(CCVars.GameMap, string.Empty);

            var map = server.System<ST14SectorSystem>().SectorMap(out _, out var cell);

            server.CfgMan.SetCVar(CCVars.GameMap, forced);
            server.CfgMan.SetCVar(ST14CVars.SectorRounds, false);

            Assert.That(cell, Is.GreaterThanOrEqualTo(0), "The war has no target sector.");
            Assert.That(map, Is.Not.Null, "The target sector has no map.");
        });
    }

    /// <summary>
    /// The sector template is closed off by the storm and has spawn points for both sides and places for queens
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

            const int size = ST14SectorTemplateCommand.Size;
            const int inner = size - 2 * ST14SectorTemplateCommand.StormDepth;
            Assert.Multiple(() =>
            {
                Assert.That(storm, Is.EqualTo(size * size - inner * inner), "The storm does not close off the whole edge.");
                Assert.That(points, Does.Contain("ST14Engineer"), "No landing points for the troopers.");
                Assert.That(points, Does.Contain("ST14Bug"), "No spawn points for the bugs.");
                Assert.That(points, Does.Contain("ST14BrainBug"), "No spawn point for the brain bug.");
                Assert.That(queens, Is.GreaterThanOrEqualTo(2), "Too few queen places.");
            });

            entMan.DeleteEntity(map.Value);
        });
    }
}
