using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._ST14.Blood;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

[TestOf(typeof(ST14BloodDecalsComponent))]
public sealed class ST14BloodDecalsTest : InteractionTest
{
    private static readonly ProtoId<DamageTypePrototype> SlashDamageType = "Slash";

    /// <summary>
    /// A cut human splatters and drips blood as decals and never spills a puddle
    /// </summary>
    [Test]
    public async Task HumanBleedsDecalsTest()
    {
        // Floor all around, a decal is never put on space
        for (var x = -3; x <= 4; x++)
        {
            for (var y = -3; y <= 4; y++)
            {
                await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, y + 0.5f))), MapData.Grid);
            }
        }

        await SpawnTarget("MobHuman");
        var human = STarget!.Value;
        Assert.That(SEntMan.HasComponent<ST14BloodDecalsComponent>(human), "Humans have no blood decals.");

        var damage = new DamageSpecifier(Server.ProtoMan.Index(SlashDamageType), FixedPoint2.New(25));
        await Server.WaitPost(() =>
        {
            SEntMan.System<DamageableSystem>().TryChangeDamage(human, damage, ignoreResistances: true, origin: SPlayer);
            SEntMan.System<BloodstreamSystem>().TryBleedOut(human, 20);
        });
        await RunTicks(5);

        var count = SEntMan.EntityQuery<DecalChunkComponent>().Sum(chunk => chunk.Decals.Count);
        Assert.That(count, Is.GreaterThanOrEqualTo(3), "A cut and bleeding human left too few blood decals.");

        Assert.That(SEntMan.EntityQuery<PuddleComponent>().Any(), Is.False, "Human blood spilled into a puddle.");
    }
}
