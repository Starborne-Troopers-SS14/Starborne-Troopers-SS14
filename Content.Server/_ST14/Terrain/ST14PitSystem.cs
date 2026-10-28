using System.Numerics;
using Content.Shared._ST14.Terrain;
using Content.Shared.Chasm;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._ST14.Terrain;

public sealed partial class ST14PitSystem : EntitySystem
{
    [Dependency] private ChasmSystem _chasm = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14PitComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<ST14PitComponent, EndCollideEvent>(OnEndCollide);
    }

    // Collide events come per fixture pair, so count them: the walker leaves only when the last one ends.
    private void OnStartCollide(Entity<ST14PitComponent> ent, ref StartCollideEvent args)
    {
        if (args.OtherBody.BodyType == BodyType.Static)
            return;

        ent.Comp.Touching[args.OtherEntity] = ent.Comp.Touching.GetValueOrDefault(args.OtherEntity) + 1;
    }

    private void OnEndCollide(Entity<ST14PitComponent> ent, ref EndCollideEvent args)
    {
        if (!ent.Comp.Touching.TryGetValue(args.OtherEntity, out var count))
            return;

        if (count <= 1)
            ent.Comp.Touching.Remove(args.OtherEntity);
        else
            ent.Comp.Touching[args.OtherEntity] = count - 1;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ST14PitComponent, ChasmComponent, FixturesComponent>();
        while (query.MoveNext(out var uid, out var pit, out var chasm, out var fixtures))
        {
            if (pit.Touching.Count == 0)
                continue;

            var inv = _transform.GetInvWorldMatrix(uid);
            List<EntityUid>? done = null;
            foreach (var other in pit.Touching.Keys)
            {
                if (TerminatingOrDeleted(other) || HasComp<ChasmFallingComponent>(other))
                {
                    (done ??= new()).Add(other);
                    continue;
                }

                var local = Vector2.Transform(_transform.GetWorldPosition(other), inv);
                if (!Inside(fixtures, local))
                    continue;

                if (_chasm.StartFalling((uid, chasm), other) != null)
                    (done ??= new()).Add(other);
            }

            if (done == null)
                continue;

            foreach (var other in done)
            {
                pit.Touching.Remove(other);
            }
        }
    }

    /// <summary>
    /// Whether a point in the pit's local space is inside one of its fixtures
    /// </summary>
    private static bool Inside(FixturesComponent fixtures, Vector2 local)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0).Contains(local))
                return true;
        }

        return false;
    }
}
