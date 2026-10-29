using System.Linq;
using Content.Client._ST14.MainHub.Galaxy;
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

                if (sector.Ring > 0)
                    Assert.That(planet.Angle, Is.InRange(sector.Start, sector.End), $"{planet.ID} lies outside the angles of {sector.ID}.");

                foreach (var link in planet.Links)
                {
                    Assert.That(protoMan.HasIndex(link), $"{planet.ID} links to an unknown planet {link}.");
                }
            }
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
                Assert.That(war.CountOf(ST14GalaxyFaction.Federation), Is.GreaterThan(0));
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
