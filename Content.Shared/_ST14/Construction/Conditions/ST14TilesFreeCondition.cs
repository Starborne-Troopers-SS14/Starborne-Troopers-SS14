using System.Numerics;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Maps;
using Content.Shared.Physics;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared._ST14.Construction.Conditions;

/// <summary>
/// ST14: for something that takes more than its own tile (the steps of the stairs, the second half of a gate): the
/// tile it goes on and the tiles at <see cref="Offsets"/> from it are solid floor with nothing in the way. The offsets
/// are for it facing south (no rotation) and turn with the way it is built facing.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14TilesFreeCondition : IConstructionCondition
{
    [DataField]
    public List<Vector2i> Offsets = new();

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (!entMan.TrySystem<TurfSystem>(out var turf))
            return false;

        var angle = direction.ToAngle();
        if (!Free(turf, location))
            return false;

        foreach (var offset in Offsets)
        {
            var turned = angle.RotateVec(new Vector2(offset.X, offset.Y));
            if (!Free(turf, location.Offset(new Vector2(MathF.Round(turned.X), MathF.Round(turned.Y)))))
                return false;
        }

        return true;
    }

    private static bool Free(TurfSystem turf, EntityCoordinates location)
    {
        return turf.TryGetTileRef(location, out var tileRef) &&
               !turf.IsSpace(tileRef.Value) &&
               !turf.IsTileBlocked(tileRef.Value, CollisionGroup.Impassable);
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry { Localization = "st14-construction-condition-tiles-free" };
    }
}
