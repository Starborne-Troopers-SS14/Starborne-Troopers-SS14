using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.Atmos;
using Content.Server.Body.Components;
using Content.Shared._ST14.CCVar;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14AtmosToggleSystem))]
public sealed class ST14AtmosToggleTest : GameTest
{
    /// <summary>
    /// With atmospherics off a spawned person stops breathing instead of suffocating
    /// </summary>
    [Test]
    public async Task NoBreathingWithoutAtmosTest()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid human = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(ST14CVars.Atmospherics, false);
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        await server.WaitRunTicks(server.Timing.TickRate * 3);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<RespiratorComponent>(human), Is.False, "A person still breathes with atmospherics off.");
            server.CfgMan.SetCVar(ST14CVars.Atmospherics, true);
        });
    }
}
