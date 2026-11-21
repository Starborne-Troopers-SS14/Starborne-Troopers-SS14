using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Light.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.ZLevels;

public sealed partial class ST14ZLevelLightingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZLevelMapNetworkUpdatedEvent>(OnNetworkUpdated);
    }

    private void OnNetworkUpdated(CEZLevelMapNetworkUpdatedEvent args)
    {
        if (!TryComp<CEZMapNetworkComponent>(args.Network, out var network) ||
            !network.ZLevels.TryGetValue(0, out var ground) ||
            ground is not { } source)
            return;

        foreach (var (depth, map) in network.ZLevels)
        {
            if (depth <= 0 || map is not { } target)
                continue;

            CopyIfMissing<MapLightComponent>(source, target);
            CopyIfMissing<LightCycleComponent>(source, target);
        }
    }

    private void CopyIfMissing<T>(EntityUid source, EntityUid target) where T : IComponent
    {
        if (HasComp<T>(target) || !TryComp<T>(source, out var component))
            return;

        CopyComp(source, target, component);
    }
}
