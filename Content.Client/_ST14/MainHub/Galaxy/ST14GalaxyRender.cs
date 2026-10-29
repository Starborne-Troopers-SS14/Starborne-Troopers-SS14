using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// Colours of the sides of the war, the same everywhere on the galaxy map
/// </summary>
public static class ST14GalaxyColors
{
    public static readonly Color Federation = Color.FromHex("#4FA3E0");
    public static readonly Color Arachnid = Color.FromHex("#E8752A");
    public static readonly Color Contested = Color.FromHex("#E8C24A");
    public static readonly Color Unexplored = Color.FromHex("#6B7785");
    public static readonly Color Line = Color.FromHex("#8CB4E6");

    public static Color Of(ST14GalaxyFaction faction)
    {
        return faction switch
        {
            ST14GalaxyFaction.Federation => Federation,
            ST14GalaxyFaction.Arachnid => Arachnid,
            ST14GalaxyFaction.Contested => Contested,
            _ => Unexplored,
        };
    }
}

/// <summary>
/// Growing list of vertices drawn in one call. Colours are given in sRGB and stored linear, the way the renderer wants them
/// </summary>
public sealed class ST14VertexBuffer
{
    private DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[1024];
    private int _count;

    public void Clear()
    {
        _count = 0;
    }

    public void Add(Vector2 position, Vector2 uv, Color color)
    {
        AddLinear(position, uv, Color.FromSrgb(color));
    }

    private void AddLinear(Vector2 position, Vector2 uv, Color linear)
    {
        if (_count == _vertices.Length)
            Array.Resize(ref _vertices, _vertices.Length * 2);

        _vertices[_count++] = new DrawVertexUV2DColor(position, uv, linear);
    }

    public void Add(Vector2 position, Color color)
    {
        Add(position, new Vector2(0.5f, 0.5f), color);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        Add(a, color);
        Add(b, color);
        Add(c, color);
    }

    /// <summary>
    /// Triangle whose texture is laid in screen space, a repeating texture keeps one pattern across all shapes
    /// </summary>
    public void PatternTriangle(Vector2 a, Vector2 b, Vector2 c, float period, Color color)
    {
        Add(a, a / period, color);
        Add(b, b / period, color);
        Add(c, c / period, color);
    }

    public void Triangle(Vector2 a, Color ca, Vector2 b, Color cb, Vector2 c, Color cc)
    {
        Add(a, ca);
        Add(b, cb);
        Add(c, cc);
    }

    /// <summary>
    /// Square sprite of the whole texture
    /// </summary>
    public void Sprite(Vector2 center, float radius, Color color)
    {
        SpriteLinear(center, radius, Color.FromSrgb(color));
    }

    /// <summary>
    /// Sprite with a colour that is already linear, for the thousands of stars converted once
    /// </summary>
    public void SpriteLinear(Vector2 center, float radius, Color linear)
    {
        var tl = center + new Vector2(-radius, -radius);
        var tr = center + new Vector2(radius, -radius);
        var bl = center + new Vector2(-radius, radius);
        var br = center + new Vector2(radius, radius);
        AddLinear(tl, new Vector2(0, 0), linear);
        AddLinear(tr, new Vector2(1, 0), linear);
        AddLinear(br, new Vector2(1, 1), linear);
        AddLinear(tl, new Vector2(0, 0), linear);
        AddLinear(br, new Vector2(1, 1), linear);
        AddLinear(bl, new Vector2(0, 1), linear);
    }

    public void Line(Vector2 a, Vector2 b, float width, Color color)
    {
        var dir = b - a;
        var length = dir.Length();
        if (length < 0.001f)
            return;

        var normal = new Vector2(-dir.Y, dir.X) / length * (width / 2);
        Triangle(a - normal, a + normal, b + normal, color);
        Triangle(a - normal, b + normal, b - normal, color);
    }

    public void Circle(Vector2 center, float radius, Color color, int segments = 20)
    {
        var prev = center + new Vector2(radius, 0);
        for (var i = 1; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            Triangle(center, prev, next, color);
            prev = next;
        }
    }

    /// <summary>
    /// Ring between two radii, the colour fades from the inner to the outer edge
    /// </summary>
    public void Ring(Vector2 center, float inner, float outer, Color innerColor, Color outerColor, int segments = 32)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = MathF.Tau * i / segments;
            var a1 = MathF.Tau * (i + 1) / segments;
            var d0 = new Vector2(MathF.Cos(a0), MathF.Sin(a0));
            var d1 = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
            Triangle(center + d0 * inner, innerColor, center + d0 * outer, outerColor, center + d1 * outer, outerColor);
            Triangle(center + d0 * inner, innerColor, center + d1 * outer, outerColor, center + d1 * inner, innerColor);
        }
    }

    public void Draw(DrawingHandleScreen handle, Texture texture)
    {
        // the renderer takes at most 65532 vertices in one call, bigger buffers go in whole triangles
        const int chunk = 60000;
        for (var start = 0; start < _count; start += chunk)
        {
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, texture,
                _vertices.AsSpan(start, Math.Min(chunk, _count - start)));
        }
    }
}

/// <summary>
/// Textures the galaxy map makes at runtime: the soft glow sprite and the planet surfaces.
/// Kept per screen, a texture belongs to the client that made it
/// </summary>
public sealed class ST14GalaxyTextures
{
    public const int SurfaceWidth = 512;
    public const int SurfaceHeight = 256;

    private readonly Dictionary<string, Texture> _surfaces = new();
    private Texture? _glow;
    private Texture? _hatch;

    /// <summary>
    /// Diagonal stripes that cover enemy space, like on the war table of the original game. Tiles without a seam
    /// </summary>
    public Texture Hatch
    {
        get
        {
            if (_hatch != null)
                return _hatch;

            const int size = 32;
            using var image = new Image<Rgba32>(size, size);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // distance to the nearest stripe along the diagonal, a stripe every 8 pixels
                    var d = ((x + y) % 8 + 8) % 8;
                    var a = d < 3 ? 1f : d == 3 || d == 7 ? 0.45f : 0.12f;
                    image[x, y] = new Rgba32(255, 255, 255, (byte) (a * 255));
                }
            }

            _hatch = Texture.LoadFromImage(image, "st14-galaxy-hatch", Smooth);
            return _hatch;
        }
    }

    private static readonly TextureLoadParameters Smooth = new()
    {
        SampleParameters = new TextureSampleParameters { Filter = true, WrapMode = TextureWrapMode.Repeat },
        Srgb = true,
    };

    /// <summary>
    /// White disc that fades out to the edge
    /// </summary>
    public Texture Glow
    {
        get
        {
            if (_glow != null)
                return _glow;

            const int size = 64;
            using var image = new Image<Rgba32>(size, size);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f).Length() / (size / 2f);
                    var a = MathF.Max(0, 1 - d);
                    a = a * a * (3 - 2 * a);
                    image[x, y] = new Rgba32(255, 255, 255, (byte) (a * 255));
                }
            }

            _glow = Texture.LoadFromImage(image, "st14-galaxy-glow", Smooth with { SampleParameters = new TextureSampleParameters { Filter = true } });
            return _glow;
        }
    }

    /// <summary>
    /// Equirectangular map of the planet surface: x is the longitude, y goes from the north pole to the south one
    /// </summary>
    public Texture Surface(ST14GalaxyPlanetPrototype planet)
    {
        if (_surfaces.TryGetValue(planet.ID, out var cached))
            return cached;

        var colors = planet.Surface.Count > 0 ? planet.Surface : new List<Color> { Color.Gray, Color.White };
        using var image = new Image<Rgba32>(SurfaceWidth, SurfaceHeight);
        var seed = planet.Seed * 97 + 11;
        var offset = new Vector3(ST14Noise.Hash(seed, 1) * 50, ST14Noise.Hash(seed, 2) * 50, ST14Noise.Hash(seed, 3) * 50);

        for (var y = 0; y < SurfaceHeight; y++)
        {
            var lat = MathF.PI / 2 - (y + 0.5f) / SurfaceHeight * MathF.PI;
            for (var x = 0; x < SurfaceWidth; x++)
            {
                var lon = (x + 0.5f) / SurfaceWidth * MathF.Tau;
                var p = ST14Sphere.Point(lon, lat);

                // continents from the broad noise, ridges and small detail on top
                var broad = ST14Noise.Fbm(p * 1.8f + offset, 5, seed);
                var ridge = 1 - MathF.Abs(ST14Noise.Fbm(p * 4.5f + offset, 3, seed + 7) * 2 - 1);
                var height = Math.Clamp((broad - 0.5f) * 2.4f + 0.5f + (ridge - 0.5f) * 0.25f, 0, 1);
                var detail = ST14Noise.Value(p * 40f + offset, seed + 3) - 0.5f;

                var color = Gradient(colors, height);
                var shade = 1 + detail * 0.16f;
                image[x, y] = new Rgba32(
                    (byte) Math.Clamp(color.R * shade * 255, 0, 255),
                    (byte) Math.Clamp(color.G * shade * 255, 0, 255),
                    (byte) Math.Clamp(color.B * shade * 255, 0, 255),
                    255);
            }
        }

        var texture = Texture.LoadFromImage(image, $"st14-galaxy-{planet.ID}", Smooth);
        _surfaces[planet.ID] = texture;
        return texture;
    }

    private static Color Gradient(List<Color> colors, float t)
    {
        if (colors.Count == 1)
            return colors[0];

        var scaled = t * (colors.Count - 1);
        var index = Math.Min((int) scaled, colors.Count - 2);
        return Color.InterpolateBetween(colors[index], colors[index + 1], scaled - index);
    }
}
