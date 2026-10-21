using System.Linq;
using Content.Shared._ST14.Construction;
using Robust.Client.GameObjects;
using Robust.Client.Placement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14BlueprintPlacementHijack : PlacementHijack
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public readonly ST14EngineerBuildablePrototype Buildable;

    public override bool CanRotate => Buildable.CanRotate;

    public ST14BlueprintPlacementHijack(ST14EngineerBuildablePrototype buildable)
    {
        IoCManager.InjectDependencies(this);
        Buildable = buildable;
    }

    public override bool HijackPlacementRequest(EntityCoordinates coordinates)
    {
        _entMan.System<ST14MultitoolSystem>().PlaceBlueprint(Buildable, coordinates, Manager.Direction);
        return true;
    }

    public override void StartHijack(PlacementManager manager)
    {
        base.StartHijack(manager);

        if (!_prototype.TryIndex(Buildable.Result, out var result))
            return;

        var sprites = _entMan.System<SpriteSystem>();
        manager.CurrentTextures = sprites.GetPrototypeTextures(result).ToList();

        if (manager.CurrentPlacementOverlayEntity is { } overlay &&
            result.TryComp<SpriteComponent>(out var resultSprite, _entMan.ComponentFactory))
            sprites.SetOffset(overlay, resultSprite.Offset);
    }
}
