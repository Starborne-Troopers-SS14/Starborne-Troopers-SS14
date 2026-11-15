#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._ST14.ZLevels.Core;
using Content.Shared._ST14.ZLevels.Core.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._ST14;

/// <summary>
/// Z-level physics checks. <see cref="FallThroughOpeningTest"/> is a plain behaviour test. The
/// <c>Explicit</c> ones are measuring tools: a deterministic trace hash (run it before and after a
/// change to prove the simulation did not change) and timing/allocation probes of the hot paths.
/// </summary>
[TestOf(typeof(ST14ZLevelsSystem))]
public sealed class ST14ZLevelPerfTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ST14ZPerfDummy
  components:
  - type: ST14ZPhysics
  - type: Physics
    bodyType: Dynamic
  - type: Sprite

- type: entity
  id: ST14ZPerfStep
  components:
  - type: Transform
    anchored: true
  - type: ST14ZLevelHighGround
    heightCurve: [ 0.5 ]
";

    public override PoolSettings PoolSettings => new() { Connected = true, Fresh = true };

    private const int Half = 32;

    /// <summary>
    /// Two-level network: the lower map is floored everywhere, the upper map only where x &gt;= 0,
    /// so everything over x &lt; 0 on the upper level stands over an opening.
    /// </summary>
    private (EntityUid Lower, EntityUid Upper, Entity<MapGridComponent> UpperGrid) MakeNetwork()
    {
        var map = SEntMan.System<SharedMapSystem>();
        var zLevels = SEntMan.System<ST14ZLevelsSystem>();
        var tileDef = Server.ResolveDependency<ITileDefinitionManager>()["Plating"];

        var lower = map.CreateMap(out var lowerId);
        var upper = map.CreateMap(out var upperId);
        // CE z-levels are planet maps: the map entity is the grid (TileChangedEvent is raised on it).
        Entity<MapGridComponent> lowerGrid = (lower, SEntMan.EnsureComponent<MapGridComponent>(lower));
        Entity<MapGridComponent> upperGrid = (upper, SEntMan.EnsureComponent<MapGridComponent>(upper));

        var lowerTiles = new List<(Vector2i, Tile)>();
        var upperTiles = new List<(Vector2i, Tile)>();
        for (var x = -Half; x < Half; x++)
        {
            for (var y = -Half; y < Half; y++)
            {
                lowerTiles.Add((new Vector2i(x, y), new Tile(tileDef.TileId)));
                if (x >= 0)
                    upperTiles.Add((new Vector2i(x, y), new Tile(tileDef.TileId)));
            }
        }

        map.SetTiles(lowerGrid, lowerTiles);
        map.SetTiles(upperGrid, upperTiles);

        var network = zLevels.CreateMapNetwork();
        Assert.That(zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int> { { lower, 0 }, { upper, 1 } }));
        return (lower, upper, upperGrid);
    }

    private static Vector2 SpawnPos(int i, int count)
    {
        // Deterministic spread over the whole upper level, half over the opening, half over floor.
        var side = (int) MathF.Ceiling(MathF.Sqrt(count));
        var x = -Half + 0.5f + (i % side) * (2f * Half - 1f) / side;
        var y = -Half + 0.5f + (i / side) * (2f * Half - 1f) / side;
        return new Vector2(x, y);
    }

    [Test]
    public async Task FallThroughOpeningTest()
    {
        EntityUid lower = default, upper = default, overHole = default, overFloor = default;

        await Server.WaitPost(() =>
        {
            (lower, upper, _) = MakeNetwork();
            var upperId = SEntMan.GetComponent<MapComponent>(upper).MapId;
            overHole = SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(new Vector2(-5.5f, 0.5f), upperId));
            overFloor = SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(new Vector2(5.5f, 0.5f), upperId));
        });

        // Ground height is cached on movement, so nudge them like something that was just dropped or walked.
        await Server.WaitPost(() =>
        {
            var xformSys = SEntMan.System<SharedTransformSystem>();
            xformSys.SetWorldPosition(overHole, new Vector2(-5.4f, 0.5f));
            xformSys.SetWorldPosition(overFloor, new Vector2(5.4f, 0.5f));
        });

        await RunTicksSync(120);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(overHole).MapUid, Is.EqualTo(lower), "Entity over the opening did not fall a level.");
            Assert.That(SEntMan.GetComponent<TransformComponent>(overFloor).MapUid, Is.EqualTo(upper), "Entity on the floor fell through it.");
            Assert.That(SComp<ST14ZPhysicsComponent>(overHole).CurrentZLevel, Is.EqualTo(0));
            Assert.That(SComp<ST14ZPhysicsComponent>(overFloor).CurrentZLevel, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The ground under a standing body is cached per tile. Removing the floor under it must still drop it,
    /// walking inside the same tile must keep it up, and stairs placed under it must lift its ground.
    /// </summary>
    [Test]
    public async Task GroundCacheInvalidationTest()
    {
        EntityUid lower = default, upper = default, faller = default, walker = default, climber = default;
        Entity<MapGridComponent> upperGrid = default;
        var xformSys = SEntMan.System<SharedTransformSystem>();
        var map = SEntMan.System<SharedMapSystem>();

        await Server.WaitPost(() =>
        {
            (lower, upper, upperGrid) = MakeNetwork();
            var upperId = SEntMan.GetComponent<MapComponent>(upper).MapId;
            faller = SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(new Vector2(3.5f, 3.5f), upperId));
            walker = SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(new Vector2(6.2f, 3.5f), upperId));
            climber = SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(new Vector2(9.5f, 3.5f), upperId));
        });

        await Server.WaitPost(() =>
        {
            xformSys.SetWorldPosition(faller, new Vector2(3.4f, 3.5f));
            xformSys.SetWorldPosition(walker, new Vector2(6.3f, 3.5f));
            xformSys.SetWorldPosition(climber, new Vector2(9.4f, 3.5f));
        });
        await RunTicksSync(30);

        await Server.WaitPost(() =>
        {
            // Floor disappears under a body that does not move.
            map.SetTile(upperGrid, new Vector2i(3, 3), Tile.Empty);
            // Moving inside the same tile reuses the cached flat ground.
            xformSys.SetWorldPosition(walker, new Vector2(6.8f, 3.5f));
            // Stairs (constant height 0.5) appear under a body that does not move.
            SEntMan.SpawnEntity("ST14ZPerfStep", new EntityCoordinates(upper, new Vector2(9.5f, 3.5f)));
        });
        await RunTicksSync(120);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<TransformComponent>(faller).MapUid, Is.EqualTo(lower), "Body did not fall when the floor under it was removed.");
            Assert.That(SEntMan.GetComponent<TransformComponent>(walker).MapUid, Is.EqualTo(upper), "Body fell while walking on intact floor.");
            Assert.That(SComp<ST14ZPhysicsComponent>(walker).CachedGroundHeight, Is.EqualTo(0f));
            Assert.That(SComp<ST14ZPhysicsComponent>(climber).CachedGroundHeight, Is.EqualTo(0.5f).Within(1e-4), "Stairs placed under a body did not raise its ground.");
            Assert.That(SComp<ST14ZPhysicsComponent>(climber).LocalPosition, Is.EqualTo(0.5f).Within(0.05), "Body did not step up onto the stairs.");
        });
    }

    /// <summary>
    /// Prints a hash of the full z-physics state of 300 bodies over 240 ticks while they are being
    /// pushed around, jumped and dropped. Must print the same value before and after an optimisation.
    /// </summary>
    [Test, Explicit]
    public async Task DeterministicTraceHash()
    {
        const int count = 300;
        var ents = new List<EntityUid>();
        var hash = new HashCode();
        var xformSys = SEntMan.System<SharedTransformSystem>();
        var zSys = SEntMan.System<ST14ZLevelsSystem>();

        await Server.WaitPost(() =>
        {
            var (_, upper, _) = MakeNetwork();
            var upperId = SEntMan.GetComponent<MapComponent>(upper).MapId;
            for (var i = 0; i < count; i++)
            {
                var e = SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(SpawnPos(i, count), upperId));
                ents.Add(e);
                zSys.SetZPosition(e, (i % 5) * 0.2f);
                if (i % 7 == 0)
                    zSys.SetZVelocity(e, 4f);
            }
        });

        for (var tick = 0; tick < 240; tick++)
        {
            var t = tick;
            await Server.WaitPost(() =>
            {
                for (var i = 0; i < ents.Count; i++)
                {
                    if ((i + t) % 3 != 0)
                        continue;
                    var e = ents[i];
                    var pos = xformSys.GetWorldPosition(e);
                    var dir = ((i + t / 20) % 2 == 0) ? 1f : -1f;
                    xformSys.SetWorldPosition(e, pos + new Vector2(0.05f * dir, 0.01f));
                    if ((i * 31 + t) % 97 == 0)
                        zSys.AddZVelocity(e, 3f);
                }
            });
            await RunTicksSync(1);
            await Server.WaitPost(() =>
            {
                foreach (var e in ents)
                {
                    var z = SComp<ST14ZPhysicsComponent>(e);
                    var mapUid = SEntMan.GetComponent<TransformComponent>(e).MapUid;
                    var depth = SEntMan.TryGetComponent<ST14ZMapComponent>(mapUid, out var zm) ? zm.Depth : 99;
                    hash.Add(BitConverter.SingleToInt32Bits(z.LocalPosition));
                    hash.Add(BitConverter.SingleToInt32Bits(z.Velocity));
                    hash.Add(z.CurrentZLevel);
                    hash.Add(z.Sleeping);
                    hash.Add(depth);
                }
                hash.Add(zSys.ActiveBodies.Count);
            });
        }

        // HashCode is randomly seeded per process, so also build a stable FNV hash of the final state.
        ulong fnv = 1469598103934665603UL;
        await Server.WaitPost(() =>
        {
            foreach (var e in ents)
            {
                var z = SComp<ST14ZPhysicsComponent>(e);
                var p = xformSys.GetWorldPosition(e);
                foreach (var v in new[] { BitConverter.SingleToInt32Bits(z.LocalPosition), BitConverter.SingleToInt32Bits(z.Velocity), z.CurrentZLevel, z.Sleeping ? 1 : 0, BitConverter.SingleToInt32Bits(p.X), BitConverter.SingleToInt32Bits(p.Y) })
                {
                    fnv ^= (uint) v;
                    fnv *= 1099511628211UL;
                }
            }
        });

        TestContext.Out.WriteLine($"ZTRACE final-state FNV = {fnv:X16}");
    }

    /// <summary>
    /// Times the server-side hot paths with many z-physics bodies: move events, the z-physics step,
    /// and bulk tile changes on a z-level map. Prints ms and allocated bytes per iteration.
    /// </summary>
    [Test, Explicit]
    [TestCase(1000)]
    [TestCase(3000)]
    public async Task ServerHotPathTiming(int count)
    {
        var ents = new List<EntityUid>();
        var xformSys = SEntMan.System<SharedTransformSystem>();
        var zSys = SEntMan.System<ST14ZLevelsSystem>();
        var map = SEntMan.System<SharedMapSystem>();
        var tileDef = Server.ResolveDependency<ITileDefinitionManager>()["Plating"];
        Entity<MapGridComponent> upperGrid = default;

        await Server.WaitPost(() =>
        {
            var (_, upper, grid) = MakeNetwork();
            upperGrid = grid;
            var upperId = SEntMan.GetComponent<MapComponent>(upper).MapId;
            for (var i = 0; i < count; i++)
            {
                var p = SpawnPos(i, count);
                // Keep everyone over the floor so nobody leaves the upper map during the probe.
                ents.Add(SEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(new Vector2(MathF.Abs(p.X) + 0.5f, p.Y), upperId)));
            }
        });
        await RunTicksSync(5);

        const int iterations = 60;
        double moveMs = 0, stepMs = 0, tileMs = 0;
        long moveBytes = 0, stepBytes = 0, tileBytes = 0;

        await Server.WaitPost(() =>
        {
            // Warm-up so JIT is out of the numbers.
            for (var w = 0; w < 5; w++)
            {
                foreach (var e in ents)
                    xformSys.SetWorldPosition(e, xformSys.GetWorldPosition(e) + new Vector2(0.001f, 0));
                zSys.Update(1f / 60f);
            }

            var sw = new Stopwatch();
            for (var it = 0; it < iterations; it++)
            {
                var d = (it % 2 == 0 ? 1 : -1) * 0.01f;

                var b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                foreach (var e in ents)
                    xformSys.SetWorldPosition(e, xformSys.GetWorldPosition(e) + new Vector2(d, 0));
                sw.Stop();
                moveMs += sw.Elapsed.TotalMilliseconds;
                moveBytes += GC.GetAllocatedBytesForCurrentThread() - b0;

                b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                zSys.Update(1f / 60f);
                sw.Stop();
                stepMs += sw.Elapsed.TotalMilliseconds;
                stepBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            }

            // Turning in place (players face the mouse every frame): rotation-only moves, then the z step.
            double turnMs = 0;
            long turnBytes = 0;
            for (var it = 0; it < iterations; it++)
            {
                var rot = new Angle(0.1 * (it + 1));
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                foreach (var e in ents)
                    xformSys.SetLocalRotation(e, rot);
                zSys.Update(1f / 60f);
                sw.Stop();
                turnMs += sw.Elapsed.TotalMilliseconds;
                turnBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            }

            TestContext.Out.WriteLine($"ZPERF server N={count} turn+zstep: {turnMs / iterations:F3} ms/iter, {turnBytes / iterations} B/iter");

            // Bulk tile change: re-floor a 32x32 block of the upper level (TileChangedEvent with 1024 entries).
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < 32; x++)
            for (var y = -16; y < 16; y++)
                tiles.Add((new Vector2i(x, y), new Tile(tileDef.TileId, variant: 0)));
            var tilesAlt = new List<(Vector2i, Tile)>();
            foreach (var (idx, _) in tiles)
                tilesAlt.Add((idx, new Tile(tileDef.TileId, variant: 1)));

            // Warm-up of the tile path too.
            map.SetTiles(upperGrid, tilesAlt);
            map.SetTiles(upperGrid, tiles);
            zSys.Update(1f / 60f);

            const int tileIterations = 10;
            for (var it = 0; it < tileIterations; it++)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                map.SetTiles(upperGrid, it % 2 == 0 ? tilesAlt : tiles);
                sw.Stop();
                tileMs += sw.Elapsed.TotalMilliseconds;
                tileBytes += GC.GetAllocatedBytesForCurrentThread() - b0;
                zSys.Update(1f / 60f);
            }

            TestContext.Out.WriteLine($"ZPERF server N={count} move: {moveMs / iterations:F3} ms/iter, {moveBytes / iterations} B/iter");
            TestContext.Out.WriteLine($"ZPERF server N={count} zstep: {stepMs / iterations:F3} ms/iter, {stepBytes / iterations} B/iter, active={zSys.ActiveBodies.Count}");
            TestContext.Out.WriteLine($"ZPERF server N={count} tiles(1024): {tileMs / tileIterations:F3} ms/call, {tileBytes / tileIterations} B/call");
        });
    }

    /// <summary>
    /// Times the client per-frame sprite passes (pre/post animation) with many z-physics sprites.
    /// </summary>
    [Test, Explicit]
    [TestCase(3000)]
    public async Task ClientFramePassTiming(int count)
    {
        await Client.WaitPost(() =>
        {
            var map = CEntMan.System<SharedMapSystem>();
            map.CreateMap(out var mapId);
            var ents = new List<EntityUid>();
            for (var i = 0; i < count; i++)
                ents.Add(CEntMan.SpawnEntity("ST14ZPerfDummy", new MapCoordinates(SpawnPos(i, count), mapId)));

            // A tenth of them in the air, like a few jumping / thrown things on a busy screen.
            for (var i = 0; i < count; i += 10)
                CEntMan.GetComponent<ST14ZPhysicsComponent>(ents[i]).LocalPosition = 0.5f;

            var sysMan = CEntMan.EntitySysManager;
            EntitySystem? pre = null, post = null;
            foreach (var type in sysMan.GetEntitySystemTypes())
            {
                if (type.Name == "ST14ClientZLevelsPreAnimSystem")
                    pre = (EntitySystem) sysMan.GetEntitySystem(type);
                else if (type.Name == "ST14ClientZLevelsPostAnimSystem")
                    post = (EntitySystem) sysMan.GetEntitySystem(type);
            }

            Assert.That(pre, Is.Not.Null);
            Assert.That(post, Is.Not.Null);

            for (var w = 0; w < 10; w++)
            {
                pre!.FrameUpdate(0.016f);
                post!.FrameUpdate(0.016f);
            }

            const int frames = 200;
            var sw = Stopwatch.StartNew();
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < frames; f++)
            {
                pre!.FrameUpdate(0.016f);
                post!.FrameUpdate(0.016f);
            }
            sw.Stop();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - b0;

            TestContext.Out.WriteLine($"ZPERF client N={count} pre+post anim pass: {sw.Elapsed.TotalMilliseconds / frames:F3} ms/frame, {bytes / frames} B/frame");
        });
    }

    /// <summary>
    /// Times the per-frame "can the level below be seen" scan the viewport runs for each level, on a
    /// screen fully covered by opaque floor (the worst case: every tile is read). Uncached is the old
    /// algorithm; cached is what the viewport uses now while the view stays on the same tiles.
    /// </summary>
    [Test, Explicit]
    public async Task ClientTileScanTiming()
    {
        await Client.WaitPost(() =>
        {
            var zSys = CEntMan.System<Content.Client._ST14.ZLevels.Core.ST14ClientZLevelsSystem>();
            var map = CEntMan.System<SharedMapSystem>();
            var tileDef = Client.ResolveDependency<ITileDefinitionManager>()["Plating"];
            var mapUid = map.CreateMap(out var mapId);
            Entity<MapGridComponent> grid = (mapUid, CEntMan.EnsureComponent<MapGridComponent>(mapUid));
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -Half; x < Half; x++)
            for (var y = -Half; y < Half; y++)
                tiles.Add((new Vector2i(x, y), new Tile(tileDef.TileId)));
            map.SetTiles(grid, tiles);

            // Roughly a 1080p view at default zoom: 30 x 17 tiles.
            var min = new Vector2(-15.3f, -8.6f);
            var max = new Vector2(15.3f, 8.6f);
            Assert.That(zSys.AnyEmptyOrTransparentTile(mapUid, mapId, min, max, useCache: false), Is.False);
            Assert.That(zSys.AnyEmptyOrTransparentTile(mapUid, mapId, min, max), Is.False);

            const int frames = 2000;
            var sw = Stopwatch.StartNew();
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < frames; f++)
                zSys.AnyEmptyOrTransparentTile(mapUid, mapId, min, max, useCache: false);
            sw.Stop();
            TestContext.Out.WriteLine($"ZPERF client tile scan uncached: {sw.Elapsed.TotalMilliseconds * 1000 / frames:F2} us/call, {(GC.GetAllocatedBytesForCurrentThread() - b0) / frames} B/call");

            sw.Restart();
            b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var f = 0; f < frames; f++)
                zSys.AnyEmptyOrTransparentTile(mapUid, mapId, min, max);
            sw.Stop();
            TestContext.Out.WriteLine($"ZPERF client tile scan cached: {sw.Elapsed.TotalMilliseconds * 1000 / frames:F2} us/call, {(GC.GetAllocatedBytesForCurrentThread() - b0) / frames} B/call");

            // Correctness: a tile change must invalidate the cache.
            map.SetTile(grid, new Vector2i(0, 0), Tile.Empty);
            Assert.That(zSys.AnyEmptyOrTransparentTile(mapUid, mapId, min, max), Is.True, "Cached tile scan did not notice a tile change.");
        });
    }
}
