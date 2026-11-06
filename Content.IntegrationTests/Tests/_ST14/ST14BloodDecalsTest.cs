using System.Linq;
using System.Numerics;
using Content.Client.Decals;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._ST14.Blood;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
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
        await PlaceFloor();

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

    /// <summary>
    /// A body going down leaves its pools once, whatever way it goes, and one big loss of blood makes only a few drops
    /// </summary>
    [Test]
    public async Task PoolsAndDropsAreLimitedTest()
    {
        await PlaceFloor();
        await SpawnTarget("MobHuman");
        var human = STarget!.Value;
        var comp = SEntMan.GetComponent<ST14BloodDecalsComponent>(human);

        var mobState = SEntMan.System<MobStateSystem>();
        await Server.WaitPost(() => mobState.ChangeMobState(human, MobState.Critical));
        await RunTicks(1);
        Assert.That(CountDecals(comp.PoolTag), Is.EqualTo(comp.CriticalPools));

        await Server.WaitPost(() => mobState.ChangeMobState(human, MobState.Dead));
        await RunTicks(1);
        Assert.That(CountDecals(comp.PoolTag), Is.EqualTo(comp.DeathPools), "The pools of the critical condition were not counted.");

        await Server.WaitPost(() => SEntMan.System<BloodstreamSystem>().TryBleedOut(human, 200));
        await RunTicks(1);
        Assert.That(CountDecals(comp.DripTag), Is.EqualTo(4), "One big loss of blood made too many drops.");
        Assert.That(comp.Lost, Is.LessThan(comp.DripVolume), "The blood past the limit of drops was kept.");

        await Server.WaitPost(() => SEntMan.System<BloodstreamSystem>().SpillAllSolutions(human));
        await RunTicks(1);
        Assert.That(CountDecals(comp.PoolTag), Is.EqualTo(comp.GibPools), "A gibbed body added pools on top of the ones it left.");
        Assert.That(CountDecals(comp.SplatterTag), Is.EqualTo(comp.GibSplatters));
    }

    /// <summary>
    /// The server places blood by a size it cannot measure itself, it has to match the sprites
    /// </summary>
    [Test]
    public async Task DecalCenterMatchesSpritesTest()
    {
        var comp = new ST14BloodDecalsComponent();
        var placement = CEntMan.System<DecalPlacementSystem>();

        await Client.WaitAssertion(() =>
        {
            var found = 0;
            foreach (var decal in Client.ProtoMan.EnumeratePrototypes<DecalPrototype>())
            {
                if (!decal.Tags.Contains(comp.SplatterTag) && !decal.Tags.Contains(comp.DripTag) && !decal.Tags.Contains(comp.PoolTag))
                    continue;

                found++;
                Assert.That(placement.GetDecalHalfSize(decal.ID), Is.EqualTo(comp.DecalCenter), $"{decal.ID} is not the size the server expects.");
            }

            Assert.That(found, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// Floor all around, a decal is never put on space
    /// </summary>
    private async Task PlaceFloor()
    {
        for (var x = -3; x <= 4; x++)
        {
            for (var y = -3; y <= 4; y++)
            {
                await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, y + 0.5f))), MapData.Grid);
            }
        }
    }

    private int CountDecals(string tag)
    {
        var count = 0;
        foreach (var chunk in SEntMan.EntityQuery<DecalChunkComponent>())
        {
            count += chunk.Decals.Values.Count(decal => Server.ProtoMan.Index<DecalPrototype>(decal.Id).Tags.Contains(tag));
        }

        return count;
    }
}
