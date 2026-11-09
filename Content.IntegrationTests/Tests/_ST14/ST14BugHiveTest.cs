using Content.IntegrationTests.Fixtures;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._ST14;

[TestFixture]
[TestOf(typeof(ST14HiveMemberComponent))]
public sealed class ST14BugHiveTest : GameTest
{
    /// <summary>
    /// Every bug a player can evolve into keeps its voice: emote, pain and death sounds come from several parents,
    /// and a lost field makes the bug silent only after it evolves
    /// </summary>
    [Test]
    public async Task CastesHaveVoicesTest()
    {
        var pair = Pair;
        var protoManager = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            foreach (var caste in protoManager.EnumeratePrototypes<ST14HiveCastePrototype>())
            {
                if (caste.Entity is not { } entity)
                    continue;

                var proto = protoManager.Index(entity);
                Assert.That(proto.Components.TryGetComponent("ST14HiveMember", out var comp),
                    $"{entity} of caste {caste.ID} is not a hive member.");

                var member = (ST14HiveMemberComponent) comp!;
                Assert.Multiple(() =>
                {
                    Assert.That(member.Caste?.Id, Is.EqualTo(caste.ID), $"{entity} has the wrong caste.");
                    Assert.That(member.EmoteSounds, Is.Not.Null, $"{entity} has no emote sounds.");
                    Assert.That(member.PainSound, Is.Not.Null, $"{entity} has no pain sound.");
                    Assert.That(member.DeathSound, Is.Not.Null, $"{entity} has no death sound.");
                    // without it the emote wheel does nothing: ghost roles add it, evolution and jobs do not
                    Assert.That(proto.Components.ContainsKey("Emoting"), $"{entity} cannot emote.");
                    Assert.That(proto.Components.ContainsKey("Stamina"), Is.False, $"{entity} has stamina, bugs fight until they drop.");
                });
            }
        });
    }
}
