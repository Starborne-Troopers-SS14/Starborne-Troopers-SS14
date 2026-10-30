using Content.Shared._ST14.Hive.Components;
using Content.Shared.Damage;
using Robust.Client.GameObjects;

namespace Content.Client._ST14.Hive;

/// <summary>
/// A dead bug is drawn with its corpse sprite alone: the wound overlays of the live bug are hidden while damage visuals
/// are turned off, which the server does on death
/// </summary>
public sealed partial class ST14BugCorpseVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    private const string OverlayLayer = "DamageOverlay";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14HiveMemberComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<ST14HiveMemberComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite ||
            !_sprite.LayerMapTryGet((ent.Owner, sprite), OverlayLayer, out var layer, false))
            return;

        // Only hide: while alive the damage visuals system itself decides whether the overlay shows
        if (_appearance.TryGetData<bool>(ent, DamageVisualizerKeys.Disabled, out var disabled, args.Component) && disabled)
            _sprite.LayerSetVisible((ent.Owner, sprite), layer, false);
    }
}
