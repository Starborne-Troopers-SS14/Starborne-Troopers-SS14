#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._ST14.Medicine;
using Content.Shared._ST14.Medicine;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14DyingSystem))]
public sealed class ST14MedicineTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";
    private static readonly ProtoId<DamageTypePrototype> Piercing = "Piercing";

    private async Task SetBlunt(FixedPoint2 amount)
    {
        var damage = new DamageSpecifier(ProtoMan.Index(Blunt), amount);
        await Server.WaitPost(() => SEntMan.System<DamageableSystem>().SetDamage((STarget!.Value, Comp<DamageableComponent>()), damage));
        await RunTicks(3);
    }

    /// <summary>
    /// A person in crit runs out of air by themselves, and the heart stops being tracked once they get back up
    /// </summary>
    [Test]
    public async Task DyingInCritTest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");

        await SetBlunt(110);
        Assert.That(SEntMan.HasComponent<ST14DyingComponent>(STarget), "Crit did not start the heart.");

        await RunSeconds(5);
        Assert.That(SEntMan.System<DamageableSystem>().GetTotalDamage(STarget!.Value), Is.GreaterThan(FixedPoint2.New(110)), "A dying person took no damage.");

        await SetBlunt(0);
        Assert.That(SEntMan.HasComponent<ST14DyingComponent>(STarget), Is.False, "A healed person still has a dying heart.");
    }

    /// <summary>
    /// The station medicine is off: a bruise without a wound fades by itself, the damage of an open wound stays until
    /// the wound heals
    /// </summary>
    [Test]
    public async Task StockDamageFollowsWoundsTest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        var damageable = SEntMan.System<DamageableSystem>();

        await SetBlunt(10);
        await RunSeconds(10);
        Assert.That(damageable.GetTotalDamage(STarget!.Value), Is.LessThan(FixedPoint2.New(1)), "A bruise without a wound did not fade.");

        await Server.WaitPost(() =>
        {
            var uid = STarget!.Value;
            var wounds = SEntMan.GetComponent<ST14WoundsComponent>(uid);
            SEntMan.System<ST14WoundSystem>().AddWound((uid, wounds), ST14WoundKind.Puncture, ST14BodyZone.Torso, 0.5f, 0f, false);
            damageable.TryChangeDamage(uid, new DamageSpecifier(ProtoMan.Index(Piercing), FixedPoint2.New(20)), ignoreResistances: true);
        });
        await RunSeconds(5);
        Assert.That(damageable.GetTotalDamage(STarget!.Value), Is.GreaterThan(FixedPoint2.New(15)), "The damage of an open wound faded.");
    }

    /// <summary>
    /// A press on the beat is a hit, a press far from it is a miss, and only the first press of a beat counts
    /// </summary>
    [Test]
    public async Task HeartbeatPressTest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await SetBlunt(110);

        var system = SEntMan.System<ST14DyingSystem>();
        var uid = STarget!.Value;
        var dying = Comp<ST14DyingComponent>();

        await Server.WaitAssertion(() =>
        {
            dying.Result = ST14BeatResult.None;
            dying.NextBeat = STiming.CurTime;
            system.Press((uid, dying));
            Assert.That(dying.Result, Is.EqualTo(ST14BeatResult.Hit), "A press on the beat was not a hit.");

            dying.NextBeat = STiming.CurTime + TimeSpan.FromSeconds(1);
            system.Press((uid, dying));
            Assert.That(dying.Result, Is.EqualTo(ST14BeatResult.Hit), "A second press changed the result of the beat.");

            dying.Result = ST14BeatResult.None;
            system.Press((uid, dying));
            Assert.That(dying.Result, Is.EqualTo(ST14BeatResult.Miss), "A press far from the beat was not a miss.");
        });
    }

    /// <summary>
    /// The dead can be brought back only for a while
    /// </summary>
    [Test]
    public async Task ReviveWindowTest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await SetBlunt(210);

        var revivable = Comp<ST14RevivableComponent>();
        Assert.That(SEntMan.HasComponent<UnrevivableComponent>(STarget), Is.False, "The body was lost at once.");

        await Server.WaitPost(() => revivable.LostAt = STiming.CurTime);
        await RunTicks(3);
        Assert.That(SEntMan.HasComponent<UnrevivableComponent>(STarget), "The body can still be revived after the window.");
    }

    /// <summary>
    /// Fired in the green zone the shock brings back a dead person, and the player then gets the big beat
    /// </summary>
    [Test]
    public async Task GreenZoneRevivesTest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await SetBlunt(210);

        var defib = await PlaceInHands("ST14FieldDefibrillator");
        var comp = Comp<ST14FieldDefibComponent>(defib);

        await Interact();
        Assert.That(comp.Target, Is.EqualTo(STarget), "The first click did not start the charge.");

        await Server.WaitPost(() => comp.ChargeStart = STiming.CurTime - TimeSpan.FromSeconds(comp.ChargeTime * 0.7f));
        await RunTicks(1);
        await Client.WaitPost(() => CEntMan.GetComponent<ST14FieldDefibComponent>(CEntMan.GetEntity(defib)).ChargeStart = CTiming.CurTime - TimeSpan.FromSeconds(comp.ChargeTime * 0.7f));
        await Interact();

        Assert.Multiple(() =>
        {
            Assert.That(comp.Target, Is.Null, "The second click did not fire the charge.");
            Assert.That(Comp<MobStateComponent>().CurrentState, Is.EqualTo(MobState.Critical), "The shock in the green zone did not bring the person back.");
            Assert.That(Comp<ST14DyingComponent>().ReviveBeat, "There is no big beat after the shock.");
        });
    }

    /// <summary>
    /// A charge left alone overloads and brings nobody back
    /// </summary>
    [Test]
    public async Task OverloadTest()
    {
        await AddAtmosphere();
        await SpawnTarget("MobHuman");
        await SetBlunt(210);

        var defib = await PlaceInHands("ST14FieldDefibrillator");
        var comp = Comp<ST14FieldDefibComponent>(defib);

        await Interact();
        await RunSeconds(comp.ChargeTime + 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(comp.Target, Is.Null, "The charge did not end.");
            Assert.That(Comp<MobStateComponent>().CurrentState, Is.EqualTo(MobState.Dead), "An overload brought the person back.");
        });
    }
}
