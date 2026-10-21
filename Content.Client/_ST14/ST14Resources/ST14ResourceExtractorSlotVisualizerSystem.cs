using Content.Shared._ST14.ST14Resources;
using Content.Shared.Rounding;
using Robust.Client.GameObjects;

namespace Content.Client._ST14.ST14Resources;

public sealed class ST14ResourceExtractorSlotVisualizerSystem : VisualizerSystem<ST14ResourceExtractorSlotVisualsComponent>
{
    private static readonly ST14ResourceExtractorVisuals[] SlotKeys =
    {
        ST14ResourceExtractorVisuals.Slot1Fill,
        ST14ResourceExtractorVisuals.Slot2Fill,
    };

    protected override void OnAppearanceChange(EntityUid uid, ST14ResourceExtractorSlotVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || component.FillStates.Count == 0)
            return;

        Entity<SpriteComponent?> ent = (uid, args.Sprite);

        for (var i = 0; i < SlotKeys.Length && i < component.Layers.Length; i++)
        {
            if (!SpriteSystem.LayerMapTryGet(ent, component.Layers[i], out var layer, false))
                continue;

            // -1 или нет данных = слот пуст
            if (!AppearanceSystem.TryGetData(uid, SlotKeys[i], out float fraction, args.Component) || fraction < 0f)
            {
                SpriteSystem.LayerSetVisible(ent, layer, false);
                continue;
            }

            // 0 = первое состояние в списке (пустая), последнее = полная
            var level = ContentHelpers.RoundToLevels(Math.Clamp(fraction, 0f, 1f), 1, component.FillStates.Count);

            SpriteSystem.LayerSetVisible(ent, layer, true);
            SpriteSystem.LayerSetRsiState(ent, layer, component.FillStates[level]);
        }
    }
}
