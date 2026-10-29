using System.Linq;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Server._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

[TestOf(typeof(ST14GalaxyMapScreen))]
public sealed class ST14GalaxyMapTest
{
    /// <summary>
    /// Every planet lies inside its own sector and every supply line leads to a known planet
    /// </summary>
    [Test]
    public async Task PlanetsInSectorsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Client.ResolveDependency<IPrototypeManager>();

        await pair.Client.WaitAssertion(() =>
        {
            foreach (var planet in protoMan.EnumeratePrototypes<ST14GalaxyPlanetPrototype>())
            {
                Assert.That(protoMan.TryIndex(planet.Sector, out var sector), $"{planet.ID} points to an unknown sector.");

                var rings = ST14GalaxySectorPrototype.RingRadius;
                var inner = sector!.Ring == 0 ? 0 : rings[sector.Ring - 1];
                var outer = rings[sector.Ring];
                Assert.That(planet.Radius, Is.InRange(inner, outer), $"{planet.ID} lies outside the ring of {sector.ID}.");

                // a sector may run past 360 degrees
                if (sector.Ring > 0)
                {
                    var inside = planet.Angle >= sector.Start && planet.Angle <= sector.End ||
                                 planet.Angle + 360 >= sector.Start && planet.Angle + 360 <= sector.End;
                    Assert.That(inside, $"{planet.ID} lies outside the angles of {sector.ID}.");
                }

                foreach (var link in planet.Links)
                {
                    Assert.That(protoMan.HasIndex(link), $"{planet.ID} links to an unknown planet {link}.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Wins fill the bar of the target sector until it is taken, then the front moves to its neighbours and the target follows
    /// </summary>
    [Test]
    public async Task ServerWarTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var war = server.System<ST14GalaxyWarSystem>();

        await server.WaitAssertion(() =>
        {
            var planet = war.Planets.First();
            Assert.That(war.Reset(planet));
            Assert.That(war.TryGetWar(planet, out var state, out var target));
            Assert.That(target, Is.GreaterThanOrEqualTo(0), "The planet starts without a landing target.");

            Assert.That(war.ApplyResult(planet, false, false));
            Assert.That(state.Cells[target].Liberation, Is.Zero, "A lost landing must not go below zero.");
            Assert.That(state.Cells[target].Front, "A lost landing zone must stay open.");

            var rounds = 0;
            while (state.Cells[target].Faction != ST14GalaxyFaction.Federation && rounds++ < 10)
            {
                war.ApplyResult(planet, true, false);
            }

            Assert.That(rounds, Is.EqualTo((int) MathF.Ceiling(100 / ST14GalaxyWarRules.WinGain)));
            Assert.That(war.TryGetWar(planet, out state, out var next));
            Assert.That(next, Is.Not.EqualTo(target), "The target stayed on a taken sector.");
            Assert.That(state.Cells[next].Front);
            Assert.That(state.Grid.Neighbours[target].All(n => state.Cells[n].Front || state.Cells[n].Faction == ST14GalaxyFaction.Federation),
                "The neighbours of a taken sector did not open.");

            Assert.That(war.SetTarget(planet, target), Is.False, "A Federation sector can not be attacked.");
            war.Reset(planet);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The war planet splits into a closed honeycomb with a front line, and the map screen builds on the client
    /// </summary>
    [Test]
    public async Task WarPlanetTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var protoMan = pair.Client.ResolveDependency<IPrototypeManager>();

        await pair.Client.WaitAssertion(() =>
        {
            var planets = protoMan.EnumeratePrototypes<ST14GalaxyPlanetPrototype>().Where(p => p.War).ToList();
            Assert.That(planets, Is.Not.Empty, "No planet can be zoomed into.");

            foreach (var planet in planets)
            {
                var war = new ST14PlanetWar(planet, "K");
                var frequency = planet.HexFrequency;
                Assert.That(war.Cells, Has.Length.EqualTo(10 * frequency * frequency + 2));
                Assert.That(war.Grid.Corners.Count(c => c.Length == 5), Is.EqualTo(12), "A sphere of hexagons has twelve pentagons.");
                Assert.That(war.Grid.Corners.All(c => c.Length is 5 or 6));
                Assert.That(war.Cells.Any(c => c.Front), "The planet has no front line to land on.");
                if (planet.Liberation <= 0)
                {
                    // a planet the Federation has not landed on yet: no held ground and only the landing zones open
                    Assert.That(war.CountOf(ST14GalaxyFaction.Federation), Is.Zero);
                    Assert.That(war.Cells.Count(c => c.Front), Is.EqualTo(planet.LandingZones));
                }
                Assert.That(war.CountOf(ST14GalaxyFaction.Arachnid), Is.GreaterThan(0));

                // a point of the sphere picks the cell whose centre is closest
                for (var i = 0; i < war.Grid.Count; i += 17)
                {
                    Assert.That(war.Grid.Nearest(war.Grid.Centers[i]), Is.EqualTo(i));
                }
            }

            var textures = new ST14GalaxyTextures();
            var surface = textures.Surface(planets[0]);
            Assert.That(surface.Size, Is.EqualTo(new Robust.Shared.Maths.Vector2i(ST14GalaxyTextures.SurfaceWidth, ST14GalaxyTextures.SurfaceHeight)));

            var screen = new ST14GalaxyMapScreen();
            Assert.That(screen.Visible, Is.False);
            screen.Visible = true;
        });

        await pair.CleanReturnAsync();
    }
}
