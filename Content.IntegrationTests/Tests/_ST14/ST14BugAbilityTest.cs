using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

[TestOf(typeof(SharedST14BugAbilitySystem))]
public sealed class ST14BugAbilityTest : InteractionTest
{
    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    private async Task<EntityUid> SpawnAt(string proto, float x, float y)
    {
        var uid = EntityUid.Invalid;
        await Server.WaitPost(() => uid = SEntMan.SpawnAtPosition(proto, MapData.GridCoords.Offset(new Vector2(x + 0.5f, y + 0.5f))));
        return uid;
    }

    private async Task LayFloor()
    {
        for (var x = -2; x <= 8; x++)
        {
            for (var y = -2; y <= 4; y++)
            {
                await SetTile(Plating, SEntMan.GetNetCoordinates(MapData.GridCoords.Offset(new Vector2(x + 0.5f, y + 0.5f))), MapData.Grid);
            }
        }
    }

    private DamageSpecifier Damage(ProtoId<DamageTypePrototype> type, float amount)
    {
        return new DamageSpecifier(Server.ProtoMan.Index(type), FixedPoint2.New(amount));
    }

    /// <summary>
    /// A killing blow sends the tiger into its last rush instead, it shrugs off everything and dies once the rush is over
    /// </summary>
    [Test]
    public async Task LastRushTest()
    {
        await LayFloor();
        var tiger = await SpawnAt("ST14MobBugTiger", 5, 2);
        await RunTicks(5);

        var damageable = SEntMan.System<DamageableSystem>();
        var mobState = SEntMan.System<MobStateSystem>();

        await Server.WaitPost(() => damageable.TryChangeDamage(tiger, Damage(Slash, 1000), ignoreResistances: true));
        await RunTicks(2);

        var rush = SEntMan.GetComponent<ST14LastRushComponent>(tiger);
        Assert.Multiple(() =>
        {
            Assert.That(mobState.IsDead(tiger), Is.False, "The killing blow killed the tiger at once.");
            Assert.That(rush.Active, "The tiger did not rage after the killing blow.");
        });

        await Server.WaitPost(() => damageable.TryChangeDamage(tiger, Damage(Slash, 1000), ignoreResistances: true));
        await RunTicks(2);
        Assert.That(mobState.IsDead(tiger), Is.False, "The raging tiger was hurt.");

        await RunSeconds((float) rush.Duration.TotalSeconds + 0.5f);
        Assert.That(mobState.IsDead(tiger), "The tiger outlived its last rush.");
    }

    /// <summary>
    /// A trooper in crit is executed on the spot and the tiger heals
    /// </summary>
    [Test]
    public async Task ExecuteTest()
    {
        await LayFloor();
        var human = await SpawnAt("MobHuman", 4, 2);
        var tiger = await SpawnAt("ST14MobBugTiger", 5, 2);
        await RunTicks(5);

        var damageable = SEntMan.System<DamageableSystem>();
        var mobState = SEntMan.System<MobStateSystem>();

        await Server.WaitPost(() =>
        {
            damageable.TryChangeDamage(human, Damage(Blunt, 120), ignoreResistances: true);
            damageable.TryChangeDamage(tiger, Damage(Slash, 100), ignoreResistances: true);
        });
        await RunTicks(2);
        Assert.That(mobState.IsCritical(human), "The trooper is not in crit.");

        await Server.WaitPost(() =>
        {
            var ev = new ST14BugExecuteActionEvent { Performer = tiger, Target = human };
            SEntMan.EventBus.RaiseLocalEvent(tiger, ev);
        });
        await RunTicks(5);

        Assert.Multiple(() =>
        {
            Assert.That(mobState.IsDead(human), "The trooper in crit was not executed.");
            Assert.That(damageable.GetTotalDamage(tiger), Is.LessThan(FixedPoint2.New(100)), "The execution did not heal the tiger.");
        });
    }

    /// <summary>
    /// Warriors lock into a wall that cannot walk and shares its wounds, pressing again breaks it
    /// </summary>
    [Test]
    public async Task BarricadeTest()
    {
        await LayFloor();
        var first = await SpawnAt("ST14MobBugWarrior", 4, 2);
        var second = await SpawnAt("ST14MobBugWarrior", 5, 2);
        await RunTicks(5);

        var damageable = SEntMan.System<DamageableSystem>();
        var blocker = SEntMan.System<ActionBlockerSystem>();

        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(first, new ST14BugBarricadeActionEvent { Performer = first }));
        await RunTicks(2);

        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.HasComponent<ST14InBarricadeComponent>(first), "The calling warrior is not in the wall.");
            Assert.That(SEntMan.HasComponent<ST14InBarricadeComponent>(second), "The warrior next to it did not join the wall.");
            Assert.That(blocker.CanMove(first), Is.False, "A warrior of the wall can walk.");
            Assert.That(SEntMan.GetComponent<ST14InBarricadeComponent>(first).Shield, Is.Not.Null, "The wall has no shield.");
        });

        await Server.WaitPost(() => damageable.TryChangeDamage(first, Damage(Slash, 40), ignoreResistances: true));
        await RunTicks(2);

        Assert.Multiple(() =>
        {
            Assert.That(damageable.GetTotalDamage(first), Is.EqualTo(FixedPoint2.New(20)), "The wound was not shared.");
            Assert.That(damageable.GetTotalDamage(second), Is.EqualTo(FixedPoint2.New(20)), "The wound was not shared.");
        });

        await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(second, new ST14BugBarricadeActionEvent { Performer = second }));
        await RunTicks(2);

        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.HasComponent<ST14InBarricadeComponent>(first), Is.False, "The wall did not break.");
            Assert.That(SEntMan.HasComponent<ST14InBarricadeComponent>(second), Is.False, "The wall did not break.");
            Assert.That(blocker.CanMove(first), "A warrior cannot walk after the wall broke.");
        });
    }

    /// <summary>
    /// A grabbed trooper cannot walk, and dragged up to the brain bug it gets its brain sucked
    /// </summary>
    [Test]
    public async Task GrabTest()
    {
        await LayFloor();
        var human = await SpawnAt("MobHuman", 4, 2);
        var warrior = await SpawnAt("ST14MobBugWarrior", 5, 2);
        await RunTicks(5);

        var blocker = SEntMan.System<ActionBlockerSystem>();

        await Server.WaitPost(() =>
            SEntMan.EventBus.RaiseLocalEvent(warrior, new ST14BugGrabActionEvent { Performer = warrior, Target = human }));
        await RunTicks(2);

        Assert.Multiple(() =>
        {
            Assert.That(SEntMan.HasComponent<ST14HiveGrabbedComponent>(human), "The trooper was not grabbed.");
            Assert.That(blocker.CanMove(human), Is.False, "A grabbed trooper can walk.");
            Assert.That(SEntMan.GetComponent<TransformComponent>(human).ParentUid, Is.EqualTo(warrior), "The trooper is not held in the claws.");
        });

        // a shot at the trooper in the claws lands on the bug
        var damageable = SEntMan.System<DamageableSystem>();
        await Server.WaitPost(() => damageable.TryChangeDamage(human, Damage(Slash, 10), ignoreResistances: true, origin: SPlayer));
        await RunTicks(2);

        Assert.Multiple(() =>
        {
            // only the slash counts, a test map may have no air to breathe
            Assert.That(damageable.GetAllDamage(human).DamageDict.TryGetValue(Slash, out var slash) ? slash : FixedPoint2.Zero, Is.EqualTo(FixedPoint2.Zero), "The trooper in the claws was hurt.");
            Assert.That(damageable.GetTotalDamage(warrior), Is.GreaterThan(FixedPoint2.Zero), "The shot did not land on the bug.");
        });

        var brain = await SpawnAt("ST14MobBrainBug", 3, 2);
        await RunTicks(5);

        Assert.That(SEntMan.GetComponent<ST14BrainBugComponent>(brain).Sucking, "The brain bug did not start on the delivered trooper.");
    }
}
