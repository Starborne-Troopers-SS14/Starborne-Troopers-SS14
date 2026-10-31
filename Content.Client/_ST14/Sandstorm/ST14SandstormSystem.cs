using System.Numerics;
using Content.Shared._ST14.Sandstorm;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Sandstorm;

/// <summary>
/// Draws the sandstorm. The storm tiles have no sprites of their own: one overlay paints a single storm texture across all
/// of them in world space, so the wall is one continuous cloud, softens its edge towards open ground, and fills the screen
/// with dust as the player gets close
/// </summary>
public sealed partial class ST14SandstormSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly HashSet<Entity<ST14SandstormWallComponent>> _near = new();
    private ST14SandstormOverlay _screenOverlay = default!;
    private ST14SandstormWallOverlay _wallOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        // textures load on the first draw: a client without graphics, like the test one, never loads them
        _screenOverlay = new ST14SandstormOverlay(_sprite,
            new SpriteSpecifier.Rsi(new ResPath("/Textures/_ST14/Effects/Sandstorm/screen.rsi"), "screen"));
        _wallOverlay = new ST14SandstormWallOverlay(EntityManager, _resources, _lookup, _map, _transform);

        _overlays.AddOverlay(_screenOverlay);
        _overlays.AddOverlay(_wallOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_screenOverlay);
        _overlays.RemoveOverlay(_wallOverlay);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // the sprite is only an icon for menus, in the world the overlay paints the storm
        var query = EntityQueryEnumerator<ST14SandstormWallComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var wall, out var sprite, out var xform))
        {
            if (wall.SpriteHidden || xform.GridUid == null)
                continue;

            _sprite.SetVisible((uid, sprite), false);
            wall.SpriteHidden = true;
        }

        _screenOverlay.Target = HazeTarget();
        _screenOverlay.Advance(frameTime);
        _wallOverlay.Advance(frameTime);
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
}

/// <summary>
/// The storm itself. Every storm tile on screen shows the part of one big repeating texture that lies under it,
/// two layers of sand drift with the wind at different scales, gusts of wind streak over them, and on the sides that
/// face open ground a ragged band of dust spills out of the tile
/// </summary>
public sealed class ST14SandstormWallOverlay : Overlay
{
    // above the field of view: the storm is a landmark seen from afar, and the solid wall tiles occlude, which would
    // otherwise hide the very tiles the storm is made of
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private const string TexturePath = "/Textures/_ST14/Effects/Sandstorm/";

    // texture pixels per tile: the 256 px textures repeat every 4 tiles, the far layer every 8
    private const float PixelsPerTile = 64f;
    private const float FringeAlpha = 0.78f;
    private const float EdgeReach = 0.75f;

    private static readonly Vector2 NearWind = new(-38f, 6f);
    private static readonly Vector2 FarWind = new(-16f, -4f);
    private static readonly Vector2 GustWind = new(-150f, 0f);

    private static readonly (Vector2i Offset, Angle Rotation)[] Sides =
    {
        (new Vector2i(1, 0), Angle.Zero),
        (new Vector2i(0, 1), Angle.FromDegrees(90)),
        (new Vector2i(-1, 0), Angle.FromDegrees(180)),
        (new Vector2i(0, -1), Angle.FromDegrees(270)),
    };

    private readonly IEntityManager _entMan;
    private readonly IResourceCache _resources;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;

    private readonly HashSet<Entity<ST14SandstormWallComponent>> _walls = new();
    private readonly Dictionary<Vector2i, bool> _tiles = new();
    private List<Entity<MapGridComponent>> _grids = new();

    private Texture? _body;
    private Texture? _streaks;
    private Texture? _edge;
    private float _time;

    public ST14SandstormWallOverlay(IEntityManager entMan, IResourceCache resources, EntityLookupSystem lookup,
        SharedMapSystem map, SharedTransformSystem transform)
    {
        _entMan = entMan;
        _resources = resources;
        _lookup = lookup;
        _map = map;
        _transform = transform;
    }

    public void Advance(float frameTime)
    {
        // wraps long before float precision suffers, every layer repeats well within a day
        _time = (_time + frameTime) % 86400f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _body ??= _resources.GetResource<TextureResource>(new ResPath(TexturePath + "body.png")).Texture;
        _streaks ??= _resources.GetResource<TextureResource>(new ResPath(TexturePath + "streaks.png")).Texture;
        _edge ??= _resources.GetResource<TextureResource>(new ResPath(TexturePath + "edge.png")).Texture;

        var handle = args.WorldHandle;
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids);

        foreach (var grid in _grids)
        {
            var matrix = _transform.GetWorldMatrix(grid);
            Matrix3x2.Invert(matrix, out var inverse);
            var local = inverse.TransformBox(args.WorldAABB).Enlarged(1.5f);

            _walls.Clear();
            _lookup.GetLocalEntitiesIntersecting(grid.Owner, local, _walls, LookupFlags.Static | LookupFlags.Approximate);
            if (_walls.Count == 0)
                continue;

            _tiles.Clear();
            foreach (var wall in _walls)
            {
                var xform = _entMan.GetComponent<TransformComponent>(wall);
                if (!xform.Anchored)
                    continue;

                _tiles[_map.TileIndicesFor(grid.Owner, grid.Comp, xform.Coordinates)] = wall.Comp.Fringe;
            }

            handle.SetTransform(matrix);
            DrawBody(handle);
            DrawEdges(handle);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawBody(DrawingHandleWorld handle)
    {
        foreach (var (tile, fringe) in _tiles)
        {
            var box = Box2.FromDimensions(tile, Vector2.One);
            var alpha = fringe ? FringeAlpha : 1f;

            handle.DrawTextureRectRegion(_body!, box, Color.White.WithAlpha(alpha), Region(tile, PixelsPerTile, NearWind));
            // a second, bigger and slower layer of the same sand breaks the four-tile repeat and gives depth
            handle.DrawTextureRectRegion(_body!, box, Color.White.WithAlpha(alpha * 0.45f), Region(tile, PixelsPerTile / 2f, FarWind));
            handle.DrawTextureRectRegion(_streaks!, box, Color.White.WithAlpha(alpha), Region(tile, PixelsPerTile, GustWind));
        }
    }

    /// <summary>
    /// Ragged dust spilling out of every side that faces a tile without storm
    /// </summary>
    private void DrawEdges(DrawingHandleWorld handle)
    {
        var size = _edge!.Size;
        foreach (var (tile, _) in _tiles)
        {
            foreach (var (offset, rotation) in Sides)
            {
                if (_tiles.ContainsKey(tile + offset))
                    continue;

                // the band lies outside the tile on that side; the texture fades along its x, which points away from the storm
                var center = (Vector2) tile + new Vector2(0.5f) + (Vector2) offset * (0.5f + EdgeReach / 2f);
                var quad = new Box2Rotated(Box2.CenteredAround(center, new Vector2(EdgeReach, 1f)), rotation, center);

                // along the edge the texture follows the world, so neighbouring bands join; it drifts with the wind
                var along = offset.X != 0 ? tile.Y : tile.X;
                var start = along * PixelsPerTile + _time * 24f;
                var region = new UIBox2(0f, start, size.X, start + PixelsPerTile);

                handle.DrawTextureRectRegion(_edge, quad, Color.White, region);
            }
        }
    }

    /// <summary>
    /// The part of a repeating texture under a tile, shifted by the wind. Texture rows go down while world y goes up
    /// </summary>
    private UIBox2 Region(Vector2i tile, float pixelsPerTile, Vector2 wind)
    {
        var x = tile.X * pixelsPerTile + _time * wind.X;
        var y = -(tile.Y + 1) * pixelsPerTile + _time * wind.Y;
        return new UIBox2(x, y, x + pixelsPerTile, y + pixelsPerTile);
    }
}

/// <summary>
/// Dust over the whole screen: two layers of the same tileable cloud drifting with the wind at different speeds
/// </summary>
public sealed class ST14SandstormOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly SpriteSystem _sprite;
    private readonly SpriteSpecifier _textureSpec;
    private Texture? _texture;
    private float _strength;
    private float _time;

    /// <summary>
    /// Thickness the dust eases towards
    /// </summary>
    public float Target;

    public ST14SandstormOverlay(SpriteSystem sprite, SpriteSpecifier texture)
    {
        _sprite = sprite;
        _textureSpec = texture;
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
        _texture ??= _sprite.Frame0(_textureSpec);
        var handle = args.ScreenHandle;
        var bounds = args.ViewportBounds;

        DrawLayer(handle, _texture, bounds, 512f, new Vector2(90f, 12f), _strength * 0.7f);
        DrawLayer(handle, _texture, bounds, 320f, new Vector2(170f, -20f), _strength * 0.5f);
    }

    private void DrawLayer(DrawingHandleScreen handle, Texture texture, UIBox2i bounds, float size, Vector2 velocity, float alpha)
    {
        var offset = new Vector2(Repeat(_time * velocity.X, size), Repeat(_time * velocity.Y, size));
        var color = Color.White.WithAlpha(Math.Clamp(alpha, 0f, 1f));

        for (var x = bounds.Left - size + offset.X; x < bounds.Right; x += size)
        {
            for (var y = bounds.Top - size + offset.Y; y < bounds.Bottom; y += size)
            {
                handle.DrawTextureRect(texture, UIBox2.FromDimensions(new Vector2(x, y), new Vector2(size, size)), color);
            }
        }
    }

    private static float Repeat(float value, float length)
    {
        return value - MathF.Floor(value / length) * length;
    }
}
