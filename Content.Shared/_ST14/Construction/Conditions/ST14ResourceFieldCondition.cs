using Content.Shared._ST14.ST14Resources;
using Content.Shared._ST14.ST14Resources.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Construction.Conditions;

[UsedImplicitly]
[DataDefinition]
public sealed partial class ST14ResourceFieldCondition : IConstructionCondition
{
    [DataField(required: true)]
    public ProtoId<ST14ResourcePrototype> Resource;

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var map = entMan.System<SharedMapSystem>();

        if (entMan.System<SharedTransformSystem>().GetGrid(location) is not { } gridUid ||
            !entMan.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return false;

        var anchored = map.GetAnchoredEntitiesEnumerator(gridUid, grid, map.TileIndicesFor(gridUid, grid, location));
        while (anchored.MoveNext(out var uid))
        {
            if (entMan.TryGetComponent<ST14ResourceFieldComponent>(uid, out var field) && field.ResourceType == Resource)
                return true;
        }

        return false;
    }

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry { Localization = "st14-construction-condition-resource-field" };
    }
}
