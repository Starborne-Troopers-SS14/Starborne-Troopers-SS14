using System.Collections.Generic;
using System.Linq;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Server._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
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
    /// The war of the design document: the Federation lands on a region centre, a lost landing passes the turn, a win takes
    /// the sector, the bugs strike back from a neighbour, and troopers who wipe the bugs out take that neighbour too
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
            Assert.That(state.Turn, Is.EqualTo(ST14GalaxyFaction.Federation), "The Federation moves first.");
            Assert.That(state.RegionCenters, Does.Contain(target), "The first landing is not on the centre of a region.");
            Assert.That(state.Cells.Count(c => c.Front), Is.EqualTo(state.RegionCenters.Length), "Only the centres of the regions are open at first.");

            // the landing is lost: the turn passes, the bugs can only choose the sector to hold again
            Assert.That(war.ApplyResult(planet, ST14GalaxyFaction.Arachnid));
            Assert.That(state.Cells[target].Faction, Is.EqualTo(ST14GalaxyFaction.Arachnid));
            Assert.That(state.Turn, Is.EqualTo(ST14GalaxyFaction.Arachnid));

            state.Cells[target].Initialized = true;
            state.RefreshFront();
            Assert.That(state.Cells.Count(c => c.Front), Is.EqualTo(1), "The bugs should only be able to hold what was fought over.");
            Assert.That(war.SetTarget(planet, target));

            // this time the troopers win it, a time out on the next battle changes no hands
            Assert.That(war.ApplyResult(planet, ST14GalaxyFaction.Federation));
            Assert.That(state.Cells[target].Faction, Is.EqualTo(ST14GalaxyFaction.Federation));
            Assert.That(state.Cells[target].Liberation, Is.EqualTo(100));
            Assert.That(state.Turn, Is.EqualTo(ST14GalaxyFaction.Federation));

            Assert.That(war.ApplyResult(planet, null));
            Assert.That(state.Turn, Is.EqualTo(ST14GalaxyFaction.Arachnid));
            Assert.That(state.Cells[target].Faction, Is.EqualTo(ST14GalaxyFaction.Federation), "A battle that ran out of time changed the holder.");

            // the bugs strike back and pick the sector they gather in, the troopers wipe them out
            Assert.That(war.SetTarget(planet, target));
            var from = state.StagingOptions(target).Last();
            Assert.That(war.SetStaging(planet, from));
            Assert.That(war.StagingOf(planet), Is.EqualTo(from));
            Assert.That(state.StartOf(ST14GalaxyFaction.Arachnid, target, from), Is.EqualTo(from));
            Assert.That(state.StartOf(ST14GalaxyFaction.Federation, target, from), Is.EqualTo(target));
            Assert.That(war.ApplyResult(planet, ST14GalaxyFaction.Federation));
            Assert.That(state.Cells[from].Faction, Is.EqualTo(ST14GalaxyFaction.Federation), "The sector the bugs gathered in did not fall.");

            // the region goes on from what is held, only inside the region
            state.Turn = ST14GalaxyFaction.Federation;
            state.RefreshFront();
            var region = state.Cells[target].Region;
            for (var i = 0; i < state.Cells.Length; i++)
            {
                if (state.Cells[i].Region != region || !state.Cells[i].Front)
                    continue;

                Assert.That(state.Grid.Neighbours[i].Any(n => state.Cells[n].Region == region && state.Cells[n].Faction == ST14GalaxyFaction.Federation),
                    "A sector far from the held ground of the region is open.");
            }

            Assert.That(war.SetTarget(planet, target), Is.False, "A Federation sector can not be attacked by the Federation.");
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
                Assert.That(war.Cells.Any(c => c.Front), "The planet has nothing to land on.");

                // every cell lies in a region around a centre, most of them are the full seven
                var sizes = war.Cells.GroupBy(c => c.Region).Select(g => g.Count()).ToList();
                TestContext.Out.WriteLine($"{planet.ID}: {war.Cells.Length} cells, regions {string.Join(",", sizes.OrderBy(x => x))}");
                Assert.That(war.Cells.All(c => c.Region >= 0 && c.Region < war.RegionCenters.Length));
                Assert.That(sizes.Count(x => x is 6 or 7), Is.GreaterThan(sizes.Count / 2), "Most regions should be a centre with its neighbours.");
                if (planet.Regions.Count > 0)
                    Assert.That(sizes, Is.All.EqualTo(7), "A region of a planned planet is seven cells.");

                // a region is taken cell by cell from its centre, so all of it must be reachable from there
                for (var r = 0; r < war.RegionCenters.Length; r++)
                {
                    var reached = new HashSet<int> { war.RegionCenters[r] };
                    var queue = new Queue<int>(reached);
                    while (queue.TryDequeue(out var cell))
                    {
                        foreach (var n in war.Grid.Neighbours[cell].Where(n => war.Cells[n].Region == r && reached.Add(n)))
                        {
                            queue.Enqueue(n);
                        }
                    }

                    Assert.That(reached.Count, Is.EqualTo(war.Cells.Count(c => c.Region == r)), "A region falls apart.");
                }
                for (var r = 0; r < war.RegionCenters.Length; r++)
                {
                    Assert.That(war.Cells[war.RegionCenters[r]].Region, Is.EqualTo(r));
                }

                // the edges of the cells name their neighbours, and a neighbour knows the way back
                for (var i = 0; i < war.Grid.Count; i++)
                {
                    Assert.That(war.Grid.Edges[i], Is.EquivalentTo(war.Grid.Neighbours[i]));
                    Assert.That(war.Grid.Edges[i].All(n => war.Grid.Edges[n].Contains(i)));
                }

                if (planet.Liberation <= 0)
                {
                    // a planet the Federation has not landed on yet: no held ground and only the region centres open
                    Assert.That(war.CountOf(ST14GalaxyFaction.Federation), Is.Zero);
                    Assert.That(war.Cells.Count(c => c.Front), Is.EqualTo(war.RegionCenters.Length));
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
            // a painted or generated map of the whole sphere is twice as wide as it is high
            Assert.That(surface.Size.X, Is.EqualTo(surface.Size.Y * 2));

            var screen = new ST14GalaxyMapScreen();
            Assert.That(screen.Visible, Is.False);
            screen.Visible = true;

            // the command table builds every step, also with no war system to read from
            var menu = new ST14CommandMenu(textures, () => null);
            menu.Open(planets[0], new ST14PlanetWar(planets[0], "K"));
            foreach (var step in Enum.GetValues<ST14CommandMenu.Step>())
            {
                menu.ShowStep(step);
                menu.Rebuild();
            }

            menu.SetLogOpen(true);
            menu.Rebuild();
            menu.Close();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The right button means "back" all over the command table, a step at a time: the log, the tool, then the table.
    /// The planet stops the clicks that land on it, so it has to pass this one on
    /// </summary>
    [Test]
    public async Task CommandTableRightClickTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var protoMan = client.ResolveDependency<IPrototypeManager>();

        ST14CommandMenu menu = default!;
        ST14PlanetGlobe globe = default!;
        var passedOn = 0;
        await client.WaitPost(() =>
        {
            var textures = new ST14GalaxyTextures();
            var planet = protoMan.EnumeratePrototypes<ST14GalaxyPlanetPrototype>().First(p => p.War);
            menu = new ST14CommandMenu(textures, () => null);
            menu.Open(planet, new ST14PlanetWar(planet, "K"));
            menu.SetLogOpen(true);
            menu.SetTool(ST14WarMarkKind.Attack, false);

            globe = new ST14PlanetGlobe(textures);
            globe.OnRightClicked += () => passedOn++;
        });

        static GUIBoundKeyEventArgs RightClick()
        {
            return new GUIBoundKeyEventArgs(EngineKeyFunctions.UIRightClick, BoundKeyState.Down, default, default, default, default);
        }

        await client.DoGuiEvent(globe, RightClick());
        Assert.That(passedOn, Is.EqualTo(1));

        await client.DoGuiEvent(menu, RightClick());
        Assert.That(menu.Visible, Is.True, "The first right click only closes the log.");

        await client.DoGuiEvent(menu, RightClick());
        Assert.That(menu.Visible, Is.True, "The second right click only puts the tool away.");

        await client.DoGuiEvent(menu, RightClick());
        Assert.That(menu.Visible, Is.False, "With nothing left to put away the table closes.");

        await pair.CleanReturnAsync();
    }
}
