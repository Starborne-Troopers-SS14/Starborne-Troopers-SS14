using System.Numerics;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._ST14.ZLevelShooting;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._ST14.ZLevelShooting;

public sealed partial class ST14ZProjectileVisualsSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const int OverMobsDepth = (int) Content.Shared.DrawDepth.DrawDepth.OverMobs;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var up = (-_eye.CurrentEye.Rotation).RotateVec(Vector2.UnitY);
        var query = EntityQueryEnumerator<ST14ZProjectileComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flight, out var sprite, out var xform))
        {
            var (position, rotation) = _transform.GetWorldPositionRotation(xform);
            var lift = ST14ZProjectileCollisionSystem.GetHeight(flight, position) * CESharedZLevelsSystem.ZLevelOffset;
            var offset = sprite.NoRotation
                ? new Vector2(0, lift)
                : (-rotation).RotateVec(up * lift);

            _sprite.SetOffset((uid, sprite), offset);
            _sprite.SetDrawDepth((uid, sprite), OverMobsDepth);
        }
    }
}
