using System.Collections.Concurrent;
using System.Numerics;
using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ST14.MainHub;

public enum ST14HubFont : byte
{
    Regular,
    Italic,
    Medium,
    SemiBold,
    Bold,
    Slogan,
    Black,
}

/// <summary>
/// Colors, fonts and texture boxes of the hub, taken from the original menu and kept in code so the hub does not depend on the stylesheet
/// </summary>
public static class ST14HubStyle
{
    public const string UiDir = "/Textures/_ST14/Interface/Hub/UI/";
    public const string WarDir = "/Textures/_ST14/Interface/Hub/War/";

    public static readonly Color Gold = Color.FromHex("#D4C48A");
    public static readonly Color GoldLight = Color.FromHex("#F7D68C");
    public static readonly Color News = Color.FromHex("#FFD97D");
    public static readonly Color NewsBar = Color.FromHex("#C2851F");
    public static readonly Color NewsFade = Color.FromHex("#B0752933");
    public static readonly Color Orange = Color.FromHex("#FFA624");
    public static readonly Color Blue = Color.FromHex("#5C94D4");
    public static readonly Color BlueText = Color.FromHex("#E0D4A8");
    public static readonly Color Widget = Color.FromHex("#FFFFFF63");
    public static readonly Color TextShadow = Color.FromHex("#000000B0");

    // Orbitron and Exo 2 look like the original, Noto is always there and covers everything else
    private static readonly Dictionary<ST14HubFont, string[]> FontStacks = new()
    {
        [ST14HubFont.Regular] = new[] { "/Fonts/_ST14/Orbitron/Orbitron-Regular.ttf", "/Fonts/_ST14/Exo2/Exo2-Regular.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-Regular.ttf" },
        [ST14HubFont.Italic] = new[] { "/Fonts/_ST14/Exo2/Exo2-SemiBoldItalic.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-Italic.ttf" },
        [ST14HubFont.Medium] = new[] { "/Fonts/_ST14/Orbitron/Orbitron-Medium.ttf", "/Fonts/_ST14/Exo2/Exo2-Medium.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-Regular.ttf" },
        [ST14HubFont.SemiBold] = new[] { "/Fonts/_ST14/Orbitron/Orbitron-SemiBold.ttf", "/Fonts/_ST14/Exo2/Exo2-SemiBold.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf" },
        [ST14HubFont.Bold] = new[] { "/Fonts/_ST14/Orbitron/Orbitron-Bold.ttf", "/Fonts/_ST14/Exo2/Exo2-Bold.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf" },
        [ST14HubFont.Slogan] = new[] { "/Fonts/_ST14/Exo2/Exo2-BlackItalic.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-BoldItalic.ttf" },
        [ST14HubFont.Black] = new[] { "/Fonts/_ST14/Orbitron/Orbitron-Black.ttf", "/Fonts/_ST14/Exo2/Exo2-Black.ttf", "/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf" },
    };

    // Keyed by the resource cache too: integration tests run several clients in one process at the same time,
    // and a font belongs to the client that loaded it. ConcurrentDictionary because they do it on parallel threads.
    private static readonly ConcurrentDictionary<(IResourceCache, ST14HubFont, int), Font> FontCache = new();

    public static Font Font(IResourceCache cache, ST14HubFont font, int size)
    {
        if (FontCache.TryGetValue((cache, font, size), out var cached))
            return cached;

        var fonts = FontStacks[font]
            .Where(path => cache.ContentFileExists(path))
            .Select(path => (Font) new VectorFont(cache.GetResource<FontResource>(path), size))
            .ToArray();

        var result = fonts.Length == 1 ? fonts[0] : new StackedFont(fonts);
        return FontCache.GetOrAdd((cache, font, size), result);
    }

    public static Texture Tex(IResourceCache cache, string file)
    {
        return cache.GetResource<TextureResource>(UiDir + file).Texture;
    }

    /// <summary>
    /// UI textures keep the resolution of the original 4K menu and are drawn at half size, margins are in texture pixels
    /// </summary>
    public static StyleBoxTexture Patch(IResourceCache cache, string file, float left, float top, float right, float bottom, Color? modulate = null)
    {
        var box = new StyleBoxTexture
        {
            Texture = Tex(cache, file),
            Modulate = modulate ?? Color.White,
            TextureScale = new Vector2(0.5f, 0.5f),
        };
        box.SetPatchMargin(StyleBox.Margin.Left, left);
        box.SetPatchMargin(StyleBox.Margin.Top, top);
        box.SetPatchMargin(StyleBox.Margin.Right, right);
        box.SetPatchMargin(StyleBox.Margin.Bottom, bottom);
        return box;
    }

    /// <summary>
    /// Metal plate with the cut corner, used by every big button of the menu
    /// </summary>
    public static StyleBoxTexture MainButton(IResourceCache cache, Color modulate)
    {
        // patch margins would also eat the content area, the label keeps its own margins instead
        var box = Patch(cache, "main_button.png", 16, 16, 72, 72, modulate);
        box.SetContentMarginOverride(StyleBox.Margin.All, 0);
        return box;
    }

    /// <summary>
    /// Dark bar along the bottom of the screen
    /// </summary>
    public static StyleBoxTexture MainBar(IResourceCache cache)
    {
        return Patch(cache, "mainbar.png", 0, 8, 0, 128);
    }

    /// <summary>
    /// Dark glass window with a brass frame and a header strip
    /// </summary>
    public static StyleBoxTexture Window(IResourceCache cache)
    {
        var box = Patch(cache, "window_style.png", 88, 93, 94, 49);
        box.SetContentMarginOverride(StyleBox.Margin.Left, 18);
        box.SetContentMarginOverride(StyleBox.Margin.Right, 22);
        box.SetContentMarginOverride(StyleBox.Margin.Top, 4);
        box.SetContentMarginOverride(StyleBox.Margin.Bottom, 18);
        return box;
    }

    public static StyleBoxTexture UnitButton(IResourceCache cache, Color? modulate = null)
    {
        var box = Patch(cache, "unitbutton.png", 26, 26, 26, 26, modulate);
        box.SetContentMarginOverride(StyleBox.Margin.All, 0);
        return box;
    }

    public static Label Label(IResourceCache cache, string text, ST14HubFont font, int size, Color color, bool shadow = false)
    {
        var label = new ST14ScaledLabel(cache, font, size)
        {
            Text = text,
            FontColorOverride = color,
        };

        if (shadow)
        {
            label.OutlineThicknessOverride = 1.5f;
            label.OutlineColorOverride = TextShadow;
        }

        return label;
    }

    /// <summary>
    /// Applies the canvas scale to every hub label below the control
    /// </summary>
    public static void ScaleLabels(Control control, float scale)
    {
        if (control is ST14ScaledLabel label)
            label.ApplyScale(scale);

        foreach (var child in control.Children)
        {
            ScaleLabels(child, scale);
        }
    }

    public static Texture WarTex(IResourceCache cache, string file)
    {
        return cache.GetResource<TextureResource>(WarDir + file).Texture;
    }

    /// <summary>
    /// Full size texture and its .half.png copy, the copy is drawn when the texture is shown at half size or less
    /// </summary>
    public static (Texture Full, Texture Half) WarTexPair(IResourceCache cache, string file)
    {
        return (WarTex(cache, file), WarTex(cache, file[..^4] + ".half.png"));
    }

    public static Texture Pick((Texture Full, Texture Half) pair, float drawnWidth)
    {
        return drawnWidth <= pair.Half.Width ? pair.Half : pair.Full;
    }

    /// <summary>
    /// Draws a line of text centered on <paramref name="center"/> and turned by <paramref name="degrees"/> clockwise,
    /// like the slanted banner captions of the original
    /// </summary>
    public static void DrawRotatedText(DrawingHandleScreen handle, Font font, string text, Vector2 center, float degrees, Color color,
        Color? shadow = null)
    {
        var baseTransform = handle.GetTransform();
        var size = handle.GetDimensions(font, text, 1f);
        handle.SetTransform(Matrix3x2.CreateTranslation(-size / 2) * Matrix3x2.CreateRotation(MathF.PI * degrees / 180f) *
                            Matrix3x2.CreateTranslation(center) * baseTransform);

        if (shadow is { } shadowColor)
            handle.DrawString(font, new Vector2(0, size.Y * 0.06f), text, 1f, shadowColor);

        handle.DrawString(font, Vector2.Zero, text, 1f, color);
        handle.SetTransform(baseTransform);
    }

    public static StyleBoxFlat Flat(Color back, Color? border = null, float thickness = 0)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = back,
            BorderColor = border ?? Color.Transparent,
            BorderThickness = new Thickness(thickness),
        };
    }
}

/// <summary>
/// Label whose font size follows the canvas scale, so text keeps its size relative to the plates like in the original
/// </summary>
public sealed class ST14ScaledLabel : Label
{
    private readonly IResourceCache _cache;
    private readonly ST14HubFont _font;
    private readonly int _size;
    private int _applied;

    public ST14ScaledLabel(IResourceCache cache, ST14HubFont font, int size)
    {
        _cache = cache;
        _font = font;
        _size = size;
        ApplyScale(1f);
    }

    public void ApplyScale(float scale)
    {
        var size = Math.Max(8, (int) MathF.Round(_size * scale));
        if (size == _applied)
            return;

        _applied = size;
        FontOverride = ST14HubStyle.Font(_cache, _font, size);
    }
}
