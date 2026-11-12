using System.Numerics;
using Content.Shared._ST14.Weather;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Weather;

/// <summary>
/// The weather of a sector drawn over the world: falling and flying particles that live in world space, two layers of
/// clouds drifting with the wind, a tint over the view and the flashes of lightning. Everything grows with the strength
/// of the event and is dimmed by the light of the sky, so a storm at night is a dark storm
/// </summary>
public sealed class ST14WeatherOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private const int MaxParticles = 4000;
    private const int MaxEmbers = 400;
    private const float ReferenceArea = 24f * 14f;
    private const float FallHeight = 7f;

    private const string CloudDir = "/Textures/_ST14/Effects/Weather/Sky/";
    private const int MaxLayers = 4;

    // each layer has its own size, angle and pace; the sizes share no factor so the repeats never line up
    private static readonly float[] LayerTiles = { 61f, 37f, 23f, 47f };
    private static readonly float[] LayerAngle = { 0.7f, -1.1f, 0.25f, 2.3f };
    private static readonly float[] LayerSpeed = { 0.3f, 0.6f, 1f, 0.45f };
    private static readonly float[] LayerAlpha = { 0.6f, 0.55f, 0.5f, 0.45f };
    private static readonly ResPath DotPath = new("/Textures/_ST14/Effects/Weather/Sky/dot.png");

    private readonly IResourceCache _resources;
    private readonly IRobustRandom _random = IoCManager.Resolve<IRobustRandom>();
    private readonly Dictionary<string, Texture> _clouds = new();
    private Texture? _dot;

    private readonly Particle[] _particles = new Particle[MaxParticles];
    private readonly Particle[] _embers = new Particle[MaxEmbers];
    private int _active;
    private int _activeEmbers;
    private bool _seeded;

    private string? _event;
    private ST14WeatherVisual? _visual;
    private float _strength;
    private Vector2 _wind = new(1, 0);
    private float _brightness = 1f;
    private float _time;

    private Box2 _view;
    private readonly Vector2[] _layerShift = new Vector2[MaxLayers];

    private float _flash = 10f;
    private Vector2 _flashAt;

    private readonly Vertices _plain = new();
    private readonly Vertices _soft = new();

    /// <summary>
    /// A growing array of vertices drawn in one call, kept between frames so nothing is allocated per frame
    /// </summary>
    private sealed class Vertices
    {
        private DrawVertexUV2DColor[] _items = new DrawVertexUV2DColor[4096];
        public int Count;

        public void Clear()
        {
            Count = 0;
        }

        public void Add(DrawVertexUV2DColor vertex)
        {
            if (Count == _items.Length)
                Array.Resize(ref _items, _items.Length * 2);

            _items[Count++] = vertex;
        }

        public void Draw(DrawingHandleWorld handle, Texture texture)
        {
            // the renderer takes at most 65532 vertices in one call
            const int chunk = 60000;
            for (var start = 0; start < Count; start += chunk)
            {
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, texture,
                    _items.AsSpan(start, Math.Min(chunk, Count - start)));
            }
        }
    }

    private struct Particle
    {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Z;
        public float Seed;
        public float Splash;
        public float Life;

        /// <summary>
        /// The gust the particle is caught in, eased so it never jumps
        /// </summary>
        public float Gust;
    }

    public ST14WeatherOverlay(IResourceCache resources)
    {
        _resources = resources;
        ZIndex = 50;
    }

    public void Flash(Vector2 position)
    {
        _flash = 0f;
        _flashAt = position;
    }

    /// <summary>
    /// The weather to show this frame; advances the particles
    /// </summary>
    public void Set(string? id, ST14WeatherVisual? visual, float strength, Vector2 wind, float brightness, float frameTime)
    {
        _time += frameTime;
        _flash += frameTime;
        _brightness += (brightness - _brightness) * MathF.Min(1f, frameTime * 3f);
        _wind = wind.LengthSquared() > 0.001f ? Vector2.Normalize(wind) : new Vector2(1, 0);

        // the old weather keeps its look while it dies down, a new one starts from nothing
        if (id != null && id != _event)
        {
            _event = id;
            _visual = visual;
            _seeded = false;
        }

        _strength = id == null ? MathF.Max(0f, _strength - frameTime * 0.3f) : strength;
        if (_strength <= 0.001f && id == null)
        {
            _event = null;
            _visual = null;
        }

        if (_visual is not { } v || _view.Width <= 0)
            return;

        var area = _view.Width * _view.Height / ReferenceArea;
        _active = Math.Clamp((int) (v.Count * _strength * area), 0, MaxParticles);
        _activeEmbers = Math.Clamp((int) (v.Embers * _strength * area), 0, MaxEmbers);

        if (!_seeded)
        {
            _seeded = true;
            for (var i = 0; i < MaxParticles; i++)
            {
                Spawn(ref _particles[i], v, true);
            }

            for (var i = 0; i < MaxEmbers; i++)
            {
                SpawnEmber(ref _embers[i], v, true);
            }
        }

        for (var i = 0; i < _active; i++)
        {
            Step(ref _particles[i], v, frameTime);
        }

        for (var i = 0; i < _activeEmbers; i++)
        {
            StepEmber(ref _embers[i], v, frameTime);
        }

        // the layers drift with the wind at different paces, each in its own turned frame
        for (var i = 0; i < MaxLayers; i++)
        {
            var angle = LayerFrame(v, i);
            var move = _wind * v.FogSpeed * LayerSpeed[i] * frameTime;
            var shift = _layerShift[i] + new Vector2(Vector2.Dot(move, angle.ToVec()), Vector2.Dot(move, Perp(angle.ToVec())));
            _layerShift[i] = new Vector2(Repeat(shift.X, 4096f), Repeat(shift.Y, 4096f));
        }
    }

    #region Particles

    private Box2 SpawnBox => _view.Enlarged(2f);

    private Vector2 RandomIn(Box2 box)
    {
        return new Vector2(_random.NextFloat(box.Left, box.Right), _random.NextFloat(box.Bottom, box.Top));
    }

    /// <summary>
    /// The wind comes in gusts: bands of strong wind run downwind over the ground with calmer air between them, their
    /// fronts bent and slowly writhing. Returns how strong the wind is at a place, about 0.15 to 1.5, and how much it
    /// swings to the side there
    /// </summary>
    private float GustAt(Vector2 pos, float speed, out float sway)
    {
        var across = Perp(_wind);
        var along = Vector2.Dot(pos, _wind);
        var side = Vector2.Dot(pos, across);
        const float k1 = MathF.Tau / 17f;
        const float k2 = MathF.Tau / 43f;
        var bend = 1.4f * MathF.Sin(side * 0.11f + _time * 0.23f) + 0.6f * MathF.Sin(side * 0.31f - _time * 0.4f);
        // the gusts run a little slower than the grains they carry, the long swells slower still
        var w1 = MathF.Sin(k1 * along - _time * k1 * speed * 0.8f + bend);
        var w2 = MathF.Sin(k2 * along - _time * k2 * speed * 0.55f + side * 0.05f);
        sway = MathF.Sin(along * 0.21f - _time * 1.6f + side * 0.09f) * (0.5f + 0.5f * w1);
        var g = 0.55f + 0.45f * (0.5f + 0.5f * w1) + 0.2f * w2;
        return MathF.Max(0.3f, g);
    }

    private void CatchGust(ref Particle p, float speed, float dt, out float sway)
    {
        var g = GustAt(p.Pos, speed, out sway);
        p.Gust += (g - p.Gust) * MathF.Min(1f, dt * 5f);
    }

    private void Spawn(ref Particle p, ST14WeatherVisual v, bool anywhere)
    {
        p.Pos = RandomIn(SpawnBox);
        p.Gust = 1f;
        p.Seed = _random.NextFloat();
        p.Splash = 0f;
        p.Z = anywhere ? _random.NextFloat(0f, FallHeight) : FallHeight * _random.NextFloat(0.7f, 1.3f);
        p.Life = _random.NextFloat(0f, 10f);
    }

    private void Step(ref Particle p, ST14WeatherVisual v, float dt)
    {
        var speed = v.Speed * (0.75f + 0.5f * p.Seed);
        switch (v.Particles)
        {
            case ST14WeatherParticles.Rain:
            case ST14WeatherParticles.Hail:
                if (p.Splash > 0f)
                {
                    p.Splash -= dt;
                    if (p.Splash <= 0f)
                        Spawn(ref p, v, false);
                    break;
                }

                p.Z -= speed * dt;
                p.Pos += _wind * speed * 0.22f * dt;
                if (p.Z <= 0f)
                    p.Splash = v.Particles == ST14WeatherParticles.Rain ? 0.28f : 0.18f;
                break;

            case ST14WeatherParticles.Snow:
            case ST14WeatherParticles.Ash:
                if (p.Splash > 0f)
                {
                    p.Splash -= dt;
                    if (p.Splash <= 0f)
                        Spawn(ref p, v, false);
                    break;
                }

                p.Z -= speed * 0.35f * dt;
                p.Pos += (_wind * speed * 0.5f + new Vector2(MathF.Sin(_time * 1.3f + p.Seed * 20f) * 0.6f, 0f)) * dt;
                if (p.Z <= 0f)
                    p.Splash = 0.8f;
                break;

            case ST14WeatherParticles.Sand:
                // grains race along the wind in gusts: a front of fast lifted sand, then a lull where it settles
                CatchGust(ref p, v.Speed, dt, out var sandSway);
                // the pace changes only a little, or the grains would pile up into walls at the fronts; a gust shows in
                // how long and bright they are and how they swing
                p.Vel = _wind * speed * (0.75f + 0.3f * p.Gust) + Perp(_wind) * sandSway * speed * 0.14f;
                p.Pos += p.Vel * dt;
                p.Z = 0.3f + 0.5f * p.Gust + MathF.Sin(_time * 2.5f + p.Seed * 30f) * 0.3f;
                break;

            case ST14WeatherParticles.Dust:
                CatchGust(ref p, v.Speed * 3f, dt, out var dustSway);
                p.Pos += (_wind * speed * 0.25f * (0.7f + 0.5f * p.Gust) + Perp(_wind) * dustSway * 0.3f + new Vector2(0, -0.15f)) * dt;
                p.Life += dt;
                break;

            case ST14WeatherParticles.Motes:
                p.Pos += (new Vector2(MathF.Sin(_time * 0.7f + p.Seed * 9f) * 0.2f, 0.35f) + _wind * 0.2f) * dt;
                p.Life += dt;
                break;
        }

        Wrap(ref p.Pos);
    }

    private void SpawnEmber(ref Particle p, ST14WeatherVisual v, bool anywhere)
    {
        p.Seed = _random.NextFloat();
        p.Life = 0f;
        if (v.Meteors)
        {
            // falling stars come in from the side the wind blows from, high over the view
            var box = SpawnBox;
            p.Pos = RandomIn(box) - _wind * box.Width * 0.5f + new Vector2(0, box.Height * 0.5f);
            if (anywhere)
                p.Pos = RandomIn(box);

            var dir = Vector2.Normalize(_wind + new Vector2(0, -0.9f));
            p.Vel = dir * v.Speed * 3f * (0.7f + 0.6f * p.Seed);
            p.Splash = _random.NextFloat(0f, 3f);
            return;
        }

        p.Pos = RandomIn(SpawnBox);
        p.Vel = new Vector2(_random.NextFloat(-0.3f, 0.3f), _random.NextFloat(0.2f, 0.8f));
        p.Splash = 0f;
    }

    private void StepEmber(ref Particle p, ST14WeatherVisual v, float dt)
    {
        p.Life += dt;
        if (v.Meteors)
        {
            // a pause between falling stars, then a fast streak across the view
            if (p.Splash > 0f)
            {
                p.Splash -= dt;
                return;
            }

            p.Pos += p.Vel * dt;
            if (!_view.Enlarged(12f).Contains(p.Pos))
                SpawnEmber(ref p, v, false);
            return;
        }

        p.Pos += (p.Vel + _wind * 1.2f + new Vector2(MathF.Sin(_time * 3f + p.Seed * 40f) * 0.4f, 0f)) * dt;
        Wrap(ref p.Pos);
    }

    /// <summary>
    /// Particles stay where they are in the world while the camera moves; what leaves the view comes back on the
    /// other side
    /// </summary>
    private void Wrap(ref Vector2 pos)
    {
        var box = SpawnBox;
        var size = box.Size;
        if (size.X <= 0 || size.Y <= 0)
            return;

        pos.X = box.Left + Repeat(pos.X - box.Left, size.X);
        pos.Y = box.Bottom + Repeat(pos.Y - box.Bottom, size.Y);
    }

    private static float Repeat(float value, float length)
    {
        return value - MathF.Floor(value / length) * length;
    }

    #endregion

    #region Drawing

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _view = args.WorldAABB;
        return _visual != null && _strength > 0.001f || _flash < 1f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _dot ??= _resources.GetResource<TextureResource>(DotPath).Texture;

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        var view = args.WorldAABB;

        if (_visual is { } v && _strength > 0.001f)
        {
            DrawFog(handle, view, v);

            // the tint goes under the particles, and at night the dark already does most of its work
            if (v.Tint.A > 0f)
            {
                var tint = Dim(v.Tint).WithAlpha(v.Tint.A * _strength * (0.35f + 0.65f * _brightness));
                handle.DrawRect(view, tint);
            }

            _plain.Clear();
            _soft.Clear();
            for (var i = 0; i < _active; i++)
            {
                DrawParticle(ref _particles[i], v);
            }

            for (var i = 0; i < _activeEmbers; i++)
            {
                DrawEmber(ref _embers[i], v);
            }

            _plain.Draw(handle, Texture.White);
            _soft.Draw(handle, _dot);
        }

        DrawFlash(handle, view);
    }

    private Color Dim(Color color)
    {
        return new Color(color.R * _brightness, color.G * _brightness, color.B * _brightness, color.A);
    }

    private void DrawFog(DrawingHandleWorld handle, Box2 view, ST14WeatherVisual v)
    {
        if (v.Fog <= 0f || v.Clouds.Count == 0)
            return;

        // the clouds swell and thin as gusts pass
        var pulse = 0.82f + 0.18f * MathF.Sin(_time * 0.45f);
        var alpha = v.Fog * _strength * pulse;
        var first = Dim(v.FogColor);
        var second = Dim(v.FogColor2 ?? v.FogColor);
        var layers = Math.Min(MaxLayers, Math.Max(v.Clouds.Count, 3));

        for (var i = 0; i < layers; i++)
        {
            var texture = Cloud(v.Clouds[i % v.Clouds.Count]);
            var color = (i % 2 == 0 ? first : second).WithAlpha(MathF.Min(1f, alpha * LayerAlpha[i]));
            DrawCloudLayer(handle, view, texture, LayerTiles[i], LayerFrame(v, i), _layerShift[i], color);
        }
    }

    private Texture Cloud(string name)
    {
        if (!_clouds.TryGetValue(name, out var texture))
        {
            texture = _resources.GetResource<TextureResource>(new ResPath(CloudDir + name + ".png")).Texture;
            _clouds[name] = texture;
        }

        return texture;
    }

    /// <summary>
    /// The turn of a cloud layer: streaky kinds lie along the wind, round ones at a fixed slant so layers differ
    /// </summary>
    private Angle LayerFrame(ST14WeatherVisual v, int layer)
    {
        var kind = v.Clouds.Count == 0 ? string.Empty : v.Clouds[layer % v.Clouds.Count];
        var wind = new Angle(MathF.Atan2(_wind.Y, _wind.X));
        return kind is "wisps" or "ribbons" ? wind + new Angle((layer - 1) * 0.08f) : new Angle(LayerAngle[layer]);
    }

    private static Vector2 Perp(Vector2 v)
    {
        return new Vector2(-v.Y, v.X);
    }

    /// <summary>
    /// One layer of a repeating cloud texture over the view, turned by <paramref name="angle"/>,
    /// <paramref name="tiles"/> tiles to a texture
    /// </summary>
    private static void DrawCloudLayer(DrawingHandleWorld handle, Box2 view, Texture texture, float tiles, Angle angle,
        Vector2 shift, Color color)
    {
        var center = view.Center;
        var half = view.Size.Length() / 2f + 1f;
        var axis = angle.ToVec();
        var u = Vector2.Dot(center, axis) - shift.X;
        var w = Vector2.Dot(center, Perp(axis)) - shift.Y;

        var ppt = texture.Size.X / tiles;
        var left = Repeat(u - half, tiles);
        var top = Repeat(-(w + half), tiles);
        var region = new UIBox2(left * ppt, top * ppt, (left + half * 2f) * ppt, (top + half * 2f) * ppt);
        var quad = new Box2Rotated(Box2.CenteredAround(center, new Vector2(half * 2f)), angle, center);
        handle.DrawTextureRectRegion(texture, quad, color, region);
    }

    /// <summary>
    /// Particles catch what light there is: at night they dim, but less than the ground, or the rain would vanish
    /// </summary>
    private Color DimParticle(Color color)
    {
        var light = MathF.Max(_brightness, 0.45f);
        return new Color(color.R * light, color.G * light, color.B * light, color.A);
    }

    private void DrawParticle(ref Particle p, ST14WeatherVisual v)
    {
        var baseColor = DimParticle(v.Color);
        var alpha = v.Color.A * (0.55f + 0.45f * p.Seed);
        switch (v.Particles)
        {
            case ST14WeatherParticles.Rain:
            {
                if (p.Splash > 0f)
                {
                    // a ring spreading on the ground and a few droplets thrown up
                    var t = 1f - p.Splash / 0.28f;
                    var ring = 0.06f + t * 0.22f;
                    Soft(p.Pos, ring, baseColor.WithAlpha(alpha * 0.5f * (1f - t)));
                    for (var k = 0; k < 3; k++)
                    {
                        var angle = p.Seed * 20f + k * 2.1f;
                        var drop = p.Pos + new Vector2(MathF.Cos(angle), MathF.Sin(angle) * 0.6f + 0.3f) * t * 0.25f;
                        Square(drop, 0.025f, baseColor.WithAlpha(alpha * (1f - t)));
                    }

                    break;
                }

                var head = p.Pos + new Vector2(0, p.Z * 0.55f);
                var dir = Vector2.Normalize(new Vector2(_wind.X * 0.22f, -0.55f + _wind.Y * 0.22f));
                var tail = head - dir * v.Length * (0.7f + 0.6f * p.Seed);
                Line(tail, head, v.Width, baseColor.WithAlpha(0f), baseColor.WithAlpha(alpha));
                break;
            }
            case ST14WeatherParticles.Hail:
            {
                if (p.Splash > 0f)
                {
                    var t = 1f - p.Splash / 0.18f;
                    var bounce = p.Pos + new Vector2((p.Seed - 0.5f) * 0.3f * t, MathF.Sin(t * MathF.PI) * 0.18f);
                    Soft(bounce, 0.07f, baseColor.WithAlpha(alpha * (1f - t * 0.7f)));
                    break;
                }

                var head = p.Pos + new Vector2(0, p.Z * 0.55f);
                var dir = Vector2.Normalize(new Vector2(_wind.X * 0.22f, -0.55f + _wind.Y * 0.22f));
                Line(head - dir * v.Length, head, v.Width, baseColor.WithAlpha(0f), baseColor.WithAlpha(alpha * 0.8f));
                Soft(head, 0.07f, baseColor.WithAlpha(alpha));
                break;
            }
            case ST14WeatherParticles.Snow:
            case ST14WeatherParticles.Ash:
            {
                var size = v.Width * (0.6f + 0.8f * p.Seed);
                if (p.Splash > 0f)
                {
                    Soft(p.Pos, size, baseColor.WithAlpha(alpha * p.Splash / 0.8f * 0.7f));
                    break;
                }

                // a faint shadow under the flake shows how high it still is
                var at = p.Pos + new Vector2(0, p.Z * 0.55f);
                Soft(at, size, baseColor.WithAlpha(alpha));
                break;
            }
            case ST14WeatherParticles.Sand:
            {
                // most grains catch the light, some are dark dust
                if (p.Seed > 0.7f)
                    baseColor = Dim(v.FogColor).WithAlpha(1f) * new Color(0.55f, 0.5f, 0.45f);

                var at = p.Pos + new Vector2(0, p.Z * 0.3f);
                // in a gust the grains stretch into long bright streaks, in the lull they are faint short specks
                var gust = Math.Clamp((p.Gust - 0.3f) / 0.9f, 0f, 1f);
                var length = v.Length * (0.4f + 1.2f * p.Seed) * (0.45f + 0.75f * gust);
                var shimmer = 0.6f + 0.4f * MathF.Sin(_time * 6f + p.Seed * 50f);
                var way = p.Vel.LengthSquared() > 0.0001f ? Vector2.Normalize(p.Vel) : _wind;
                Line(at - way * length, at, v.Width * (0.6f + p.Seed), baseColor.WithAlpha(0f),
                    baseColor.WithAlpha(alpha * shimmer * (0.45f + 0.55f * gust)));
                break;
            }
            case ST14WeatherParticles.Dust:
            {
                var fade = 0.5f + 0.5f * MathF.Sin(p.Life * 0.8f + p.Seed * 12f);
                Soft(p.Pos, v.Width * (1f + 2f * p.Seed), baseColor.WithAlpha(alpha * fade));
                break;
            }
            case ST14WeatherParticles.Motes:
            {
                var twinkle = MathF.Max(0f, MathF.Sin(p.Life * 1.7f + p.Seed * 25f));
                Soft(p.Pos, v.Width * (0.7f + p.Seed), v.Color.WithAlpha(alpha * twinkle));
                break;
            }
        }
    }

    private void DrawEmber(ref Particle p, ST14WeatherVisual v)
    {
        // embers glow on their own, the night does not dim them
        var color = v.EmberColor;
        if (v.Meteors)
        {
            if (p.Splash > 0f)
                return;

            var dir = Vector2.Normalize(p.Vel);
            var length = 3.5f + 3f * p.Seed;
            Line(p.Pos - dir * length, p.Pos, v.Width * 2.5f, color.WithAlpha(0f), color.WithAlpha(0.9f));
            Soft(p.Pos, 0.45f, Color.White.WithAlpha(0.9f));
            Soft(p.Pos, 1.1f, color.WithAlpha(0.45f));
            return;
        }

        var flicker = 0.55f + 0.45f * MathF.Sin(p.Life * 9f + p.Seed * 30f);
        Soft(p.Pos, 0.1f + 0.08f * p.Seed, color.WithAlpha(flicker));
        Soft(p.Pos, 0.35f, color.WithAlpha(flicker * 0.25f));
    }

    private void DrawFlash(DrawingHandleWorld handle, Box2 view)
    {
        if (_flash >= 1f)
            return;

        // two flickers, the second fainter, like a real stroke
        var t = _flash;
        var f = MathF.Exp(-t * 14f) + (t > 0.11f ? 0.7f * MathF.Exp(-(t - 0.11f) * 11f) : 0f);
        handle.DrawRect(view, new Color(0.85f, 0.9f, 1f, Math.Clamp(0.55f * f, 0f, 0.8f)));

        _soft.Clear();
        Soft(_flashAt, 7f, new Color(0.75f, 0.85f, 1f, Math.Clamp(0.6f * f, 0f, 1f)));
        if (_dot != null)
            _soft.Draw(handle, _dot);
    }

    private void Line(Vector2 from, Vector2 to, float width, Color fromColor, Color toColor)
    {
        var along = to - from;
        if (along.LengthSquared() < 0.00001f)
            return;

        var side = Vector2.Normalize(new Vector2(-along.Y, along.X)) * width * 0.5f;
        var a = Color.FromSrgb(fromColor);
        var b = Color.FromSrgb(toColor);
        var uv = new Vector2(0.5f, 0.5f);
        _plain.Add(new DrawVertexUV2DColor(from - side, uv, a));
        _plain.Add(new DrawVertexUV2DColor(from + side, uv, a));
        _plain.Add(new DrawVertexUV2DColor(to + side, uv, b));
        _plain.Add(new DrawVertexUV2DColor(from - side, uv, a));
        _plain.Add(new DrawVertexUV2DColor(to + side, uv, b));
        _plain.Add(new DrawVertexUV2DColor(to - side, uv, b));
    }

    private void Square(Vector2 at, float half, Color color)
    {
        Line(at - new Vector2(half, 0), at + new Vector2(half, 0), half * 2f, color, color);
    }

    private void Soft(Vector2 at, float radius, Color color)
    {
        var c = Color.FromSrgb(color);
        var bl = at + new Vector2(-radius, -radius);
        var br = at + new Vector2(radius, -radius);
        var tr = at + new Vector2(radius, radius);
        var tl = at + new Vector2(-radius, radius);
        _soft.Add(new DrawVertexUV2DColor(bl, new Vector2(0, 1), c));
        _soft.Add(new DrawVertexUV2DColor(br, new Vector2(1, 1), c));
        _soft.Add(new DrawVertexUV2DColor(tr, new Vector2(1, 0), c));
        _soft.Add(new DrawVertexUV2DColor(bl, new Vector2(0, 1), c));
        _soft.Add(new DrawVertexUV2DColor(tr, new Vector2(1, 0), c));
        _soft.Add(new DrawVertexUV2DColor(tl, new Vector2(0, 0), c));
    }

    #endregion
}
