using System.Numerics;
using Robust.Shared.Physics.Events;

namespace Content.Shared._ST14.ZLevelShooting;

public sealed partial class ST14ZProjectileCollisionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14ZProjectileComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<ST14ZProjectileComponent> ent, ref PreventCollideEvent args)
    {
        if (ent.Comp.Target is { } target && args.OtherEntity != target)
            args.Cancelled = true;
    }

    public static float GetHeight(ST14ZProjectileComponent flight, Vector2 position)
    {
        var progress = flight.Distance > 0f
            ? Math.Clamp(Vector2.Distance(position, flight.Origin) / flight.Distance, 0f, 1f)
            : 1f;

        return MathHelper.Lerp(flight.From, flight.To, progress);
    }
}
