using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Looks of the caste abilities: trembling air around resonating warriors and raging tigers, the sinews of a living
/// barricade, the blood trails a tiger smells and the red pulse of its last rush
/// </summary>
public sealed partial class ST14BugAbilityVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private ST14HiveShimmerOverlay _shimmer = default!;
    private ST14BarricadeOverlay _barricade = default!;
    private ST14BloodScentOverlay _scent = default!;

    private readonly HashSet<EntityUid> _raging = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14BloodScentEvent>(OnBloodScent);

        _shimmer = new ST14HiveShimmerOverlay();
        _barricade = new ST14BarricadeOverlay();
        _scent = new ST14BloodScentOverlay();

        _overlays.AddOverlay(_shimmer);
        _overlays.AddOverlay(_barricade);
        _overlays.AddOverlay(_scent);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay(_shimmer);
        _overlays.RemoveOverlay(_barricade);
        _overlays.RemoveOverlay(_scent);
    }

    private void OnBloodScent(ST14BloodScentEvent args)
    {
        _scent.SetTrails(args.Trails, _timing.RealTime);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // the raging tiger glows red in the beat of its heart
        var pulse = 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 12f);
        var rage = Color.InterpolateBetween(Color.White, new Color(1f, 0.25f, 0.2f), 0.35f + 0.45f * pulse);

        var query = EntityQueryEnumerator<ST14LastRushComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var rush, out var sprite))
        {
            if (rush.Active && !_mobState.IsDead(uid))
            {
                _sprite.SetColor((uid, sprite), rage);
                _raging.Add(uid);
            }
            else if (_raging.Remove(uid))
            {
                _sprite.SetColor((uid, sprite), Color.White);
            }
        }
    }
}
