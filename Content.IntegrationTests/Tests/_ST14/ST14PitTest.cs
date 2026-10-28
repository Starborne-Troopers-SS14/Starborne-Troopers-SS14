using Content.IntegrationTests.Tests.Movement;
using Content.Shared._ST14.Terrain;
using Content.Shared.Chasm;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

[TestOf(typeof(ST14PitComponent))]
public sealed class ST14PitTest : MovementTest
{
    private readonly EntProtoId _pitProto = "ST14Pit1";

    /// <summary>
    /// Standing at the rim is safe, walking into the hole makes the player fall and get deleted
    /// </summary>
    [Test]
    public async Task PitFallTest()
    {
        await SpawnTarget(_pitProto);
        Assert.That(Delta(), Is.GreaterThan(0.5), "Player did not spawn left of the pit.");

        // The player starts on the rim, outside the hole fixtures' inner area.
        await Pair.RunSeconds(1f);
        Assert.That(HasComp<ChasmFallingComponent>(Player), Is.False, "Player fell while standing next to the pit.");

        await Move(DirectionFlag.East, 0.5f);
        Assert.That(TryComp<ChasmFallingComponent>(Player, out var falling), "Player is not falling after walking into the pit.");

        await Pair.RunSeconds((float) falling!.DeletionTime.TotalSeconds);
        AssertDeleted(Player);
    }
}
