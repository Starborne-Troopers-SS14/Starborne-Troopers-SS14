using Content.IntegrationTests.Tests.Movement;
using Content.Shared.Physics;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

public sealed class ST14RockWallTest : MovementTest
{
    private readonly EntProtoId _wallProto = "ST14WallRockFaceFront";

    /// <summary>
    /// Rock walls stop people but let bullets and lasers through
    /// </summary>
    [Test]
    public async Task RockWallBlocksWalkersNotShotsTest()
    {
        await SpawnTarget(_wallProto);
        Assert.That(Delta(), Is.GreaterThan(0.5), "Player did not spawn left of the wall.");

        await Move(DirectionFlag.East, 1f);
        Assert.That(Delta(), Is.GreaterThan(0), "Player walked through the rock wall.");

        var fixtures = Comp<FixturesComponent>(Target);
        Assert.That(fixtures.Fixtures, Is.Not.Empty);
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            Assert.That(fixture.CollisionLayer & (int) (CollisionGroup.Impassable | CollisionGroup.BulletImpassable | CollisionGroup.Opaque),
                Is.Zero, "Rock wall fixture blocks bullets or lasers.");
        }
    }
}
