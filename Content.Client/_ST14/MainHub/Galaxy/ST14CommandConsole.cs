using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// The art and the small drawing helpers of the command table. Frames, icons and portraits come from the original game
/// </summary>
public static class ST14HqArt
{
    private const string Dir = "/Textures/_ST14/Interface/Hq/";

    public static readonly Color Dark = Color.FromHex("#14171C");
    public static readonly Color Muted = new(0.68f, 0.71f, 0.75f);

    /// <summary>
    /// A metal plate that is not the one chosen: darker than the plain brass, so white text reads on it
    /// </summary>
    public static readonly Color PlateOff = Color.FromHex("#8E897E");

    public static Texture Tex(IResourceCache cache, string file)
    {
        return cache.GetResource<TextureResource>(Dir + file).Texture;
    }

    /// <summary>
    /// A frame cut into nine parts: the corners keep their size, the rest stretches
    /// </summary>
    public static StyleBoxTexture Frame(IResourceCache cache, string file, float horizontal, float vertical, float scale, Color? modulate = null)
    {
        var box = new StyleBoxTexture
        {
            Texture = Tex(cache, file),
            Modulate = modulate ?? Color.White,
            TextureScale = new Vector2(scale, scale),
        };
        box.SetPatchMargin(StyleBox.Margin.Left | StyleBox.Margin.Right, horizontal);
        box.SetPatchMargin(StyleBox.Margin.Top | StyleBox.Margin.Bottom, vertical);
        box.SetContentMarginOverride(StyleBox.Margin.All, 0);
        return box;
    }

    public static StyleBoxTexture Tile(IResourceCache cache, bool hot)
    {
        return Frame(cache, hot ? "tile_hot.png" : "tile.png", 26, 26, 0.5f);
    }

    public static StyleBoxTexture Row(IResourceCache cache, string file)
    {
        return Frame(cache, file, 24, 12, 1f);
    }

    /// <summary>
    /// Text with its left side at the point, the point is the middle of its height
    /// </summary>
    public static void TextLeft(DrawingHandleScreen handle, Font font, string text, Vector2 point, Color color)
    {
        handle.DrawString(font, point - new Vector2(0, font.GetLineHeight(1f) / 2), text, color);
    }

    /// <summary>
    /// The text cut to the width with an ellipsis if it does not fit
    /// </summary>
    public static string Fit(DrawingHandleScreen handle, Font font, string text, float width)
    {
        if (handle.GetDimensions(font, text, 1f).X <= width)
            return text;

        for (var length = text.Length - 1; length > 1; length--)
        {
            var cut = text[..length].TrimEnd() + "…";
            if (handle.GetDimensions(font, cut, 1f).X <= width)
                return cut;
        }

        return text;
    }

    /// <summary>
    /// A texture keeping its proportions, fitted into a square around the point
    /// </summary>
    public static void Icon(DrawingHandleScreen handle, Texture texture, Vector2 center, float size, Color? color = null)
    {
        var scale = size / MathF.Max(texture.Width, texture.Height);
        var drawn = new Vector2(texture.Width, texture.Height) * scale;
        handle.DrawTextureRect(texture, UIBox2.FromDimensions(center - drawn / 2, drawn), color ?? Color.White);
    }

    public static float Time => (float) IoCManager.Resolve<IGameTiming>().RealTime.TotalSeconds;
}

/// <summary>
/// Button of the table: plays the sounds of the menu and draws itself in canvas units
/// </summary>
public abstract class ST14HqButton : BaseButton
{
    protected readonly IResourceCache Cache;

    protected ST14HqButton(IResourceCache cache, ST14HubSound sound = ST14HubSound.Click)
    {
        Cache = cache;
        MuteSounds = true;
        // the right button goes through to the screen, it means "back" everywhere
        MouseFilter = MouseFilterMode.Pass;
        OnPressed += _ => Sounds.Play(sound);
    }

    private ST14HubSoundController Sounds => UserInterfaceManager.GetUIController<ST14HubSoundController>();

    protected bool Hovered => DrawMode == DrawModeEnum.Hover;

    /// <summary>
    /// The control is laid out in canvas units, this is how many pixels one of them takes now
    /// </summary>
    protected float Unit => PixelSize.Y / MathF.Max(DesignHeight, 1);

    protected abstract float DesignHeight { get; }

    protected Font FontOf(ST14HubFont font, float size)
    {
        return ST14HubStyle.Font(Cache, font, Math.Max(8, (int) MathF.Round(size * Unit)));
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();

        if (!Disabled)
            Sounds.Play(ST14HubSound.Hover);
    }
}

public enum ST14HqStepStatus : byte
{
    /// <summary>
    /// Still to be done
    /// </summary>
    Todo,
    Done,

    /// <summary>
    /// Not up to this command right now, only shown
    /// </summary>
    Locked,
}

/// <summary>
/// One step of the plan of action on the left of the table: its number or mark, its name and what is set in it
/// </summary>
public sealed class ST14HqStep : ST14HqButton
{
    private readonly StyleBoxTexture _frame;
    private readonly StyleBoxTexture _frameHot;
    private readonly Texture _check;
    private readonly Texture _lock;
    private readonly ST14VertexBuffer _buffer = new();

    public int Number;
    public string Title = string.Empty;
    public string Value = string.Empty;
    public ST14HqStepStatus Status;
    public bool Active;
    public bool ChainBelow;
    public float ChainGap;
    public Color Accent = ST14HubStyle.GoldLight;

    public ST14HqStep(IResourceCache cache) : base(cache, ST14HubSound.Tab)
    {
        _frame = ST14HqArt.Tile(cache, false);
        _frameHot = ST14HqArt.Tile(cache, true);
        _check = ST14HqArt.Tex(cache, "check.png");
        _lock = ST14HqArt.Tex(cache, "lock.png");
    }

    protected override float DesignHeight => 88;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        var u = Unit;
        var box = new UIBox2(Vector2.Zero, size);
        (Active || Hovered ? _frameHot : _frame).Draw(handle, box, UIScale);

        var badge = new Vector2(46 * u, size.Y / 2);
        _buffer.Clear();

        // the line that ties the steps into a chain
        if (ChainBelow)
            _buffer.Line(badge + new Vector2(0, 28 * u), new Vector2(badge.X, size.Y + ChainGap * u), 3 * u, Accent.WithAlpha(0.55f));

        var pulse = 0.5f + 0.5f * MathF.Sin(ST14HqArt.Time * 3.5f);
        if (Active)
            _buffer.Circle(badge, (26 + 3 * pulse) * u, Accent.WithAlpha(0.22f), 24);

        if (Status == ST14HqStepStatus.Todo)
            AddHexagon(badge, 21 * u, Active ? Accent : Accent.WithAlpha(0.35f));

        _buffer.Draw(handle, Texture.White);

        switch (Status)
        {
            case ST14HqStepStatus.Done:
                ST14HqArt.Icon(handle, _check, badge, 44 * u);
                break;
            case ST14HqStepStatus.Locked:
                ST14HqArt.Icon(handle, _lock, badge, 32 * u, Color.White.WithAlpha(0.8f));
                break;
            default:
                ST14HubStyle.DrawRotatedText(handle, FontOf(ST14HubFont.Black, 20), Number.ToString(), badge, 0,
                    Active ? ST14HqArt.Dark : Color.White);
                break;
        }

        // the active step points to its controls on the other side of the planet
        if (Active)
        {
            var pulseX = (size.X - 26 * u) + 3 * u * pulse;
            _buffer.Clear();
            _buffer.Triangle(new Vector2(pulseX + 10 * u, size.Y / 2), new Vector2(pulseX - 4 * u, size.Y / 2 - 11 * u), new Vector2(pulseX - 4 * u, size.Y / 2 + 11 * u), ST14HubStyle.GoldLight);
            _buffer.Draw(handle, Texture.White);
        }

        var left = 84 * u;
        var title = FontOf(ST14HubFont.Bold, 19);
        ST14HqArt.TextLeft(handle, title, Title, new Vector2(left, size.Y * 0.36f), Active ? ST14HubStyle.GoldLight : Color.White);

        var value = FontOf(ST14HubFont.Regular, 14);
        ST14HqArt.TextLeft(handle, value, ST14HqArt.Fit(handle, value, Value, size.X - left - (Active ? 38 : 16) * u), new Vector2(left, size.Y * 0.68f),
            Status == ST14HqStepStatus.Done ? Color.White : ST14HqArt.Muted);
    }

    private void AddHexagon(Vector2 center, float radius, Color color)
    {
        for (var i = 0; i < 6; i++)
        {
            var a = MathF.PI / 3 * i + MathF.PI / 6;
            var b = MathF.PI / 3 * (i + 1) + MathF.PI / 6;
            _buffer.Triangle(center, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, center + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius, color);
        }
    }
}

/// <summary>
/// A square tile with the sign of a planning tool. The tool in hand burns gold
/// </summary>
public sealed class ST14HqTile : ST14HqButton
{
    private readonly StyleBoxTexture _frame;
    private readonly StyleBoxTexture _frameHot;
    private readonly string _text;
    private readonly Texture _icon;
    private readonly bool _active;

    public ST14HqTile(IResourceCache cache, string text, Texture icon, bool active) : base(cache, ST14HubSound.Tab)
    {
        _frame = ST14HqArt.Tile(cache, false);
        _frameHot = ST14HqArt.Tile(cache, true);
        _text = text;
        _icon = icon;
        _active = active;
    }

    protected override float DesignHeight => 118;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        var u = Unit;
        (_active || Hovered ? _frameHot : _frame).Draw(handle, new UIBox2(Vector2.Zero, size), UIScale);
        ST14HqArt.Icon(handle, _icon, new Vector2(size.X / 2, 50 * u), (_active ? 66 : 60) * u);
        ST14HubStyle.DrawRotatedText(handle, FontOf(ST14HubFont.Medium, 13), _text, new Vector2(size.X / 2, 98 * u), 0,
            _active ? ST14HubStyle.GoldLight : Color.White, ST14HubStyle.TextShadow);
    }
}

/// <summary>
/// A line of a list on the plate with pointed ends from the mission list of the original
/// </summary>
public sealed class ST14HqRow : ST14HqButton
{
    private readonly StyleBoxTexture _plain;
    private readonly StyleBoxTexture _hot;
    private readonly StyleBoxTexture _silver;

    public string Text = string.Empty;
    public string Note = string.Empty;
    public Color TextColor = Color.White;
    public Texture? Icon;
    public bool Selected;

    public ST14HqRow(IResourceCache cache) : base(cache)
    {
        _plain = ST14HqArt.Row(cache, "row.png");
        _hot = ST14HqArt.Row(cache, "row_hot.png");
        _silver = ST14HqArt.Row(cache, "row_silver.png");
    }

    protected override float DesignHeight => 44;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        var u = Unit;
        (Selected ? _hot : Hovered ? _silver : _plain).Draw(handle, new UIBox2(Vector2.Zero, size), UIScale);

        var left = 26 * u;
        if (Icon != null)
        {
            ST14HqArt.Icon(handle, Icon, new Vector2(left + 8 * u, size.Y / 2), 22 * u);
            left += 26 * u;
        }

        var note = FontOf(ST14HubFont.Regular, 12);
        var noteWidth = Note.Length > 0 ? handle.GetDimensions(note, Note, 1f).X : 0;
        if (Note.Length > 0)
            ST14HqArt.TextLeft(handle, note, Note, new Vector2(size.X - noteWidth - 26 * u, size.Y / 2), ST14HqArt.Muted);

        var font = FontOf(ST14HubFont.Medium, Note.Length > 0 ? 16 : 13);
        ST14HqArt.TextLeft(handle, font, ST14HqArt.Fit(handle, font, Text, size.X - left - noteWidth - 40 * u), new Vector2(left, size.Y / 2),
            Selected ? ST14HubStyle.GoldLight : TextColor);
    }
}

/// <summary>
/// The score of the war: how much of the planet each side holds
/// </summary>
public sealed class ST14HqScore : Control
{
    private readonly IResourceCache _cache;
    private readonly StyleBoxTexture _frame;

    public int Federation;
    public int Hive;
    public string FederationName = string.Empty;
    public string HiveName = string.Empty;

    public ST14HqScore(IResourceCache cache)
    {
        _cache = cache;
        _frame = ST14HqArt.Tile(cache, false);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        var u = size.Y / 42f;
        var inner = new UIBox2(new Vector2(7, 6) * u, size - new Vector2(7, 6) * u);
        var total = MathF.Max(1, Federation + Hive);
        var split = inner.Left + inner.Width * Federation / total;

        _frame.Draw(handle, new UIBox2(Vector2.Zero, size), UIScale);
        handle.DrawRect(inner, ST14GalaxyColors.Arachnid.WithAlpha(0.62f));
        if (Federation > 0)
            handle.DrawRect(new UIBox2(inner.Left, inner.Top, split, inner.Bottom), ST14GalaxyColors.Federation);
        handle.DrawRect(new UIBox2(split - u, inner.Top, split + u, inner.Bottom), Color.White);

        var font = ST14HubStyle.Font(_cache, ST14HubFont.SemiBold, Math.Max(8, (int) MathF.Round(15 * u)));
        var middle = size.Y / 2;
        ST14HqArt.TextLeft(handle, font, $"{FederationName} {Federation}", new Vector2(18 * u, middle), Color.White);

        var right = $"{Hive} {HiveName}";
        ST14HqArt.TextLeft(handle, font, right, new Vector2(size.X - handle.GetDimensions(font, right, 1f).X - 18 * u, middle), Color.White);
    }
}

/// <summary>
/// The plate over the planet that says what is going on: whose turn it is, or how long the battle has left
/// </summary>
public sealed class ST14HqBanner : Control
{
    private readonly IResourceCache _cache;
    private readonly StyleBoxTexture _gold;
    private readonly StyleBoxTexture _silver;
    private readonly Texture _red;
    private readonly Texture _clock;

    public string Text = string.Empty;
    public bool Lit;
    public bool Battle;

    public ST14HqBanner(IResourceCache cache)
    {
        _cache = cache;
        _gold = ST14HqArt.Row(cache, "row_hot.png");
        _silver = ST14HqArt.Row(cache, "row_silver.png");
        _red = ST14HqArt.Tex(cache, "banner_red.png");
        _clock = ST14HqArt.Tex(cache, "icon_time.png");
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Text.Length == 0)
            return;

        var size = (Vector2) PixelSize;
        var u = size.Y / 44f;
        var pulse = 0.5f + 0.5f * MathF.Sin(ST14HqArt.Time * 3f);

        if (Battle)
        {
            var glow = new UIBox2(new Vector2(-size.X * 0.25f, -size.Y * 0.35f), new Vector2(size.X * 1.25f, size.Y * 1.35f));
            handle.DrawTextureRect(_red, glow, Color.White.WithAlpha(0.55f + 0.35f * pulse));
        }

        (Lit ? _gold : _silver).Draw(handle, new UIBox2(Vector2.Zero, size), UIScale);

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Bold, Math.Max(8, (int) MathF.Round(17 * u)));
        var color = Battle ? Color.FromHex("#FF8A6A") : Lit ? ST14HubStyle.GoldLight : Color.White;
        var center = size / 2;
        if (Battle)
        {
            var width = handle.GetDimensions(font, Text, 1f).X;
            ST14HqArt.Icon(handle, _clock, center - new Vector2(width / 2 + 18 * u, 0), 24 * u);
            center += new Vector2(14 * u, 0);
        }

        ST14HubStyle.DrawRotatedText(handle, font, Text, center, 0, color, ST14HubStyle.TextShadow);
    }
}

/// <summary>
/// The face of who reports to the commander, with a slow band running over it as on a video link
/// </summary>
public sealed class ST14HqPortrait : Control
{
    public Texture? Portrait;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (Portrait == null)
            return;

        var size = (Vector2) PixelSize;
        handle.DrawTextureRect(Portrait, new UIBox2(Vector2.Zero, size));

        var inset = size.X * 0.06f;
        var y = (ST14HqArt.Time % 4f) / 4f * (size.Y - 2 * inset) + inset;
        var band = size.Y * 0.05f;
        handle.DrawRect(new UIBox2(inset, MathF.Max(inset, y - band), size.X - inset, MathF.Min(size.Y - inset, y)), Color.White.WithAlpha(0.06f));
    }
}

/// <summary>
/// The red rubber stamp of the original, put on a decision that was made
/// </summary>
public sealed class ST14HqStamp(IResourceCache cache, string text) : Control
{
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        ST14ClassifiedStamp.DrawStamp(handle, cache, text, size / 2, size, -5f, size.Y / 54f);
    }
}

/// <summary>
/// The plate at the top of the map that opens the table. A lamp on it burns while it is this command's turn
/// </summary>
public sealed class ST14CommandTab : ST14HqButton
{
    private readonly StyleBoxTexture _plain;
    private readonly StyleBoxTexture _hot;
    private readonly ST14VertexBuffer _buffer = new();

    public string Text = string.Empty;
    public bool OurTurn;

    public ST14CommandTab(IResourceCache cache) : base(cache)
    {
        _plain = ST14HqArt.Tile(cache, false);
        _hot = ST14HqArt.Tile(cache, true);
    }

    protected override float DesignHeight => 56;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = (Vector2) PixelSize;
        var u = Unit;
        (Hovered || OurTurn ? _hot : _plain).Draw(handle, new UIBox2(Vector2.Zero, size), UIScale);

        var pulse = 0.5f + 0.5f * MathF.Sin(ST14HqArt.Time * 3.5f);
        var lamp = new Vector2(size.X - 28 * u, size.Y / 2);
        _buffer.Clear();
        if (OurTurn)
            _buffer.Circle(lamp, (7 + 3 * pulse) * u, ST14HubStyle.GoldLight.WithAlpha(0.3f), 14);
        _buffer.Circle(lamp, 4 * u, OurTurn ? ST14HubStyle.GoldLight : new Color(0.32f, 0.35f, 0.4f), 12);
        _buffer.Draw(handle, Texture.White);

        ST14HubStyle.DrawRotatedText(handle, ST14HubStyle.Font(Cache, ST14HubFont.Bold, Math.Max(8, (int) MathF.Round(17 * u))), Text,
            new Vector2(size.X / 2 - 12 * u, size.Y / 2), 0, Color.White, ST14HubStyle.TextShadow);
    }
}
