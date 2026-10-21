using Content.Shared._ST14.ST14Resources;
using Content.Shared.Rounding;
using Robust.Client.GameObjects;

namespace Content.Client._ST14.ST14Resources;

public sealed class ST14ResourceCapsuleVisualizerSystem : VisualizerSystem<ST14ResourceCapsuleVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, ST14ResourceCapsuleVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData(uid, ST14ResourceCapsuleVisuals.FillFraction, out float fraction, args.Component))
            return;

        Entity<SpriteComponent?> ent = (uid, args.Sprite);
        if (!SpriteSystem.LayerMapTryGet(ent, component.Layer, out var layer, false))
            return;

        fraction = Math.Clamp(fraction, 0f, 1f);

        // Новый режим: явный список состояний от пустой к полной
        if (component.FillStates is { Count: > 0 } states)
        {
            var index = ContentHelpers.RoundToLevels(fraction, 1, states.Count);
            SpriteSystem.LayerSetVisible(ent, layer, true);

            if (component.EmptySprite is { } emptySprite)
            {
                // Выбираем RSI по ресурсу внутри капсулы
                var rsi = emptySprite;
                if (fraction > 0f &&
                    AppearanceSystem.TryGetData(uid, ST14ResourceCapsuleVisuals.Resource, out string? resource, args.Component) &&
                    !string.IsNullOrEmpty(resource) &&
                    component.Sprites.TryGetValue(resource, out var resourceSprite))
                {
                    rsi = resourceSprite;
                }

                SpriteSystem.LayerSetRsi(ent, layer, rsi, states[index]);
            }
            else
            {
                SpriteSystem.LayerSetRsiState(ent, layer, states[index]);
            }
        }
        // Старый режим: FillBaseName + номер, при 0 слой скрыт
        else
        {
            var level = ContentHelpers.RoundToLevels(fraction, 1, component.MaxFillLevels + 1);
            if (level <= 0)
            {
                SpriteSystem.LayerSetVisible(ent, layer, false);
                return;
            }

            SpriteSystem.LayerSetVisible(ent, layer, true);
            SpriteSystem.LayerSetRsiState(ent, layer, component.FillBaseName + level);
        }

        if (component.ApplyColor &&
            AppearanceSystem.TryGetData(uid, ST14ResourceCapsuleVisuals.Color, out Color color, args.Component))
        {
            SpriteSystem.LayerSetColor(ent, layer, color);
        }
    }
}
