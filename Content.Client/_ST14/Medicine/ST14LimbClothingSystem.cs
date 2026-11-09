using System.Linq;
using Content.Client.DisplacementMap;
using Content.Shared._ST14.Medicine;
using Content.Shared.Clothing;
using Content.Shared.DisplacementMap;
using Content.Client.Inventory;
using Robust.Client.GameObjects;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// Clothing is not drawn where a limb is gone: a sleeve over a torn-off arm, a glove on a hand that is not there,
/// a trouser leg and a boot under a stump. Each worn layer gets a mask of the missing limbs that turns with the body,
/// through the same layer-aligned map the species use to fit clothing to their shape
/// </summary>
public sealed partial class ST14LimbClothingSystem : EntitySystem
{
    private const string MaskRsi = "/Textures/_ST14/Mobs/limb_mask.rsi";

    [Dependency] private DisplacementMapSystem _displacement = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<int, DisplacementData> _masks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EquipmentVisualsUpdatedEvent>(OnEquipmentVisuals);
        SubscribeLocalEvent<ST14MissingLegsComponent, ST14LimbsChangedEvent>(OnLimbsChanged);
    }

    private void OnEquipmentVisuals(EquipmentVisualsUpdatedEvent args)
    {
        if (!TryComp<ST14MissingLegsComponent>(args.Equipee, out var limbs) || limbs.MissingLimbs == 0)
            return;

        Mask(args.Equipee, limbs.MissingLimbs, args.RevealedLayers);
    }

    private void OnLimbsChanged(Entity<ST14MissingLegsComponent> ent, ref ST14LimbsChangedEvent args)
    {
        if (!TryComp<InventorySlotsComponent>(ent, out var slots))
            return;

        foreach (var layers in slots.VisualLayerKeys.Values)
        {
            Mask(ent, ent.Comp.MissingLimbs, layers);
        }
    }

    /// <summary>
    /// Puts the mask of the missing limbs on the clothing layers of one slot, or takes it off when nothing is missing
    /// </summary>
    private void Mask(EntityUid body, int missing, HashSet<string> layers)
    {
        if (!TryComp<SpriteComponent>(body, out var sprite))
            return;

        foreach (var key in layers.Where(k => !k.EndsWith("-displacement")).ToList())
        {
            if (!_sprite.LayerMapTryGet((body, sprite), key, out var index, false))
                continue;

            if (missing == 0)
            {
                if (_displacement.EnsureDisplacementIsNotOnSprite((body, sprite), key))
                    layers.Remove($"{key}-displacement");

                continue;
            }

            if (_displacement.TryAddDisplacement(MaskFor(missing), (body, sprite), index, key, out var maskKey))
                layers.Add(maskKey);
        }
    }

    private DisplacementData MaskFor(int missing)
    {
        if (_masks.TryGetValue(missing, out var data))
            return data;

        data = new DisplacementData
        {
            SizeMaps = new()
            {
                [32] = new PrototypeLayerData { RsiPath = MaskRsi, State = $"mask{missing}" },
            },
        };

        _masks[missing] = data;
        return data;
    }
}
