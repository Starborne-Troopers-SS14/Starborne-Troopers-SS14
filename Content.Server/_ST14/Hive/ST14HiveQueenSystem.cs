using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Queens lay larvae for ghosts to take every few minutes, as long as the previous ones have found players
/// </summary>
public sealed partial class ST14HiveQueenSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14HiveQueenComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ST14HiveQueenComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextLay = _timing.CurTime + ent.Comp.LayInterval;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ST14HiveQueenComponent>();
        while (query.MoveNext(out var uid, out var queen))
        {
            if (_timing.CurTime < queen.NextLay)
                continue;

            queen.NextLay = _timing.CurTime + queen.LayInterval;

            if (_mobState.IsDead(uid))
                continue;

            queen.Laid.RemoveAll(larva => TerminatingOrDeleted(larva) ||
                                          TryComp<MindContainerComponent>(larva, out var mind) && mind.HasMind);

            if (queen.Laid.Count >= queen.MaxWaitingLarvae)
                continue;

            var offset = _random.NextVector2(0.5f, 1.2f);
            var coords = _transform.GetMapCoordinates(uid).Offset(offset);
            var larva = Spawn(queen.Larva, coords);
            queen.Laid.Add(larva);

            if (queen.LaySound != null)
                _audio.PlayPvs(queen.LaySound, larva);
        }
    }
}
