using System.Numerics;
using Content.Shared._ST14.Sandstorm;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Sandstorm;

/// <summary>
/// Draws the sandstorm: every wall tile shows its own piece of one big moving storm, so the wall reads as a single cloud
/// instead of repeated tiles, and the screen fills with dust as the player gets close to it
/// </summary>
public sealed partial class ST14SandstormSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly string[] TiledLayers = { "body", "streaks" };

    // layer and the length of its animation loop in seconds, as in the RSIs: 12 frames of 0.09 and 8 frames of 0.12
    private static readonly (string Layer, float Loop)[] AnimatedLayers = { ("body", 1.08f), ("streaks", 1.08f), ("haze", 0.96f) };

    private const float FringeAlpha = 0.72f;
    private const float SyncInterval = 2f;
    private const float SyncRange = 24f;

    private float _syncTimer;

    private readonly HashSet<Entity<ST14SandstormWallComponent>> _near = new();
    private ST14SandstormOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14SandstormWallComponent, AfterAutoHandleStateEvent>(OnHandleState);

        _overlay = new ST14SandstormOverlay(_sprite.Frame0(
            new SpriteSpecifier.Rsi(new ResPath("/Textures/_ST14/Effects/Sandstorm/screen.rsi"), "screen")));
        _overlays.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<ST14SandstormWallComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wall, out var sprite, out var xform))
        {
            if (wall.Tiled || !xform.Anchored)
                continue;

            // the piece is picked by the world tile, so neighbours always join into one picture
            var pos = _transform.GetWorldPosition(xform);
            var x = Mod((int) MathF.Floor(pos.X), ST14SandstormWallComponent.Period);
            // texture rows go down while world y goes up
            var y = Mod(-(int) MathF.Floor(pos.Y) - 1, ST14SandstormWallComponent.Period);

            foreach (var layer in TiledLayers)
            {
                if (_sprite.LayerMapTryGet((uid, sprite), layer, out var index, false))
                    _sprite.LayerSetRsiState((uid, sprite), index, $"{layer}_{x}_{y}");
            }

            // the fringe is thinner: the ground shows through where troopers can still wade in
            if (_sprite.LayerMapTryGet((uid, sprite), "body", out var body, false))
                _sprite.LayerSetColor((uid, sprite), body, Color.White.WithAlpha(wall.Fringe ? FringeAlpha : 1f));

            Sync((uid, sprite));
            wall.Tiled = true;
        }

        // sprites only animate while drawn, so pieces that were off screen drift apart; put them back on one clock
        _syncTimer -= frameTime;
        if (_syncTimer <= 0f)
        {
            _syncTimer = SyncInterval;
            SyncNearPlayer();
        }

        _overlay.Target = HazeTarget();
        _overlay.Advance(frameTime);
    }

    private void OnHandleState(Entity<ST14SandstormWallComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // the depth of the tile changed, paint it again
        ent.Comp.Tiled = false;
    }

    /// <summary>
    /// Sets every animated layer of the tile to the frame the shared clock is at, so neighbouring pieces join
    /// </summary>
    private void Sync(Entity<SpriteComponent> ent)
    {
        var time = (float) _timing.RealTime.TotalSeconds;
        foreach (var (layer, loop) in AnimatedLayers)
        {
            if (_sprite.LayerMapTryGet(ent.AsNullable(), layer, out var index, false))
                _sprite.LayerSetAnimationTime(ent.AsNullable(), index, time % loop);
        }
    }

    private void SyncNearPlayer()
    {
        if (_player.LocalEntity is not { } player || !TryComp(player, out TransformComponent? xform))
            return;

        _near.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, SyncRange, _near);
        foreach (var wall in _near)
        {
            if (TryComp(wall, out SpriteComponent? sprite))
                Sync((wall, sprite));
        }
    }

    /// <summary>
    /// How thick the dust on the screen should be now: by the distance from the local player to the closest storm tile
    /// </summary>
    private float HazeTarget()
    {
        if (_player.LocalEntity is not { } player || !TryComp(player, out TransformComponent? xform) || xform.MapID == MapId.Nullspace)
            return 0f;

        var pos = _transform.GetWorldPosition(xform);
        _near.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, 8f, _near);

        var target = 0f;
        foreach (var wall in _near)
        {
            // distance to the edge of the tile, not its centre
            var distance = MathF.Max(0f, (_transform.GetWorldPosition(wall) - pos).Length() - 0.5f);
            var closeness = 1f - Math.Clamp(distance / wall.Comp.HazeRange, 0f, 1f);
            target = MathF.Max(target, closeness * closeness * wall.Comp.HazeStrength);
        }

        return target;
    }

    private static int Mod(int value, int period)
    {
        return (value % period + period) % period;
    }
}

/// <summary>
/// Dust over the whole screen: two layers of the same tileable cloud drifting with the wind at different speeds
/// </summary>
public sealed class ST14SandstormOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly Texture _texture;
    private float _strength;
    private float _time;

    /// <summary>
    /// Thickness the dust eases towards
    /// </summary>
    public float Target;

    public ST14SandstormOverlay(Texture texture)
    {
        _texture = texture;
        ZIndex = 5;
    }

    public void Advance(float frameTime)
    {
        _time += frameTime;
        // eases in and out over about a second, walking along the wall does not flicker
        _strength += (Target - _strength) * MathF.Min(1f, frameTime * 2.5f);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _strength > 0.005f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var bounds = args.ViewportBounds;

        DrawLayer(handle, bounds, 512f, new Vector2(90f, 12f), _strength * 0.7f);
        DrawLayer(handle, bounds, 320f, new Vector2(170f, -20f), _strength * 0.5f);
    }

    private void DrawLayer(DrawingHandleScreen handle, UIBox2i bounds, float size, Vector2 velocity, float alpha)
    {
        var offset = new Vector2(Repeat(_time * velocity.X, size), Repeat(_time * velocity.Y, size));
        var color = Color.White.WithAlpha(Math.Clamp(alpha, 0f, 1f));

        for (var x = bounds.Left - size + offset.X; x < bounds.Right; x += size)
        {
            for (var y = bounds.Top - size + offset.Y; y < bounds.Bottom; y += size)
            {
                handle.DrawTextureRect(_texture, UIBox2.FromDimensions(new Vector2(x, y), new Vector2(size, size)), color);
            }
        }
    }

    private static float Repeat(float value, float length)
    {
        return value - MathF.Floor(value / length) * length;
    }
}
