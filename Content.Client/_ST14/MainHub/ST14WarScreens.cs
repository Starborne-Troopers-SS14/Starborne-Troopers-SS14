using System.Linq;
using System.Numerics;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Common parts of the screens opened by "To battle!": animated background, the dark bottom bar and the exit icon in its corner
/// </summary>
public abstract partial class ST14WarScreenBase : Control
{
    [Dependency] protected IResourceCache ResCache = default!;

    protected readonly ST14AnimatedScene Scene;
    protected readonly ST14CanvasLayout Canvas;

    public event Action? OnBack;

    protected ST14WarScreenBase(ProtoId<ST14HubScenePrototype> scene)
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
        Visible = false;

        AddChild(new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black) });

        Scene = new ST14AnimatedScene { ScanlineAlpha = 0.3f };
        Scene.SetScene(scene);
        AddChild(Scene);

        Canvas = new ST14CanvasLayout { ScaleLabels = true };
        AddChild(Canvas);
    }

    /// <summary>
    /// Bottom bar of the original, added after the screen content so it covers it
    /// </summary>
    protected void AddBottomBar()
    {
        Canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(ResCache, "mask_top.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = Color.Black,
        }, new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, new Vector2(0, 110.6f), new Vector2(0.5f, 1));

        Canvas.Add(new PanelContainer { PanelOverride = ST14HubStyle.MainBar(ResCache) },
            new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, new Vector2(0, 93), new Vector2(0.5f, 1));

        var back = new ST14HubIconButton(ST14HubStyle.Tex(ResCache, "exit.png"), new Color(0.15f, 0.35f, 0.47f))
        {
            PressSound = ST14HubSound.Back,
            ToolTip = Loc.GetString("st14-hub-war-back"),
        };
        back.OnPressed += _ => GoBack();
        Canvas.AddAt(back, new Vector2(0, 1), new Vector2(62.5f, -18.2f), new Vector2(70.2f, 70.2f), new Vector2(0, 1));
    }

    /// <summary>
    /// The exit icon leaves the screen, screens with their own levels step back one level first
    /// </summary>
    protected virtual void GoBack()
    {
        OnBack?.Invoke();
    }
}

/// <summary>
/// Mode selection of the original: three tall cards on a starfield. Only the galactic war is open, the other two are shown as in development
/// </summary>
public sealed class ST14ModeSelectScreen : ST14WarScreenBase
{
    // Card centres and size of GameModeSelectionMenu, relative to the screen centre
    private const float CardStep = 650f;
    private const float CardY = -69.35f;
    private static readonly Vector2 CardSize = new(599.9f, 838.2f);

    public event Action? OnGalacticWar;

    public ST14ModeSelectScreen() : base("ST14HubModes")
    {
        AddCard(0, "card_campaign.png", "st14-hub-war-mode-galactic", false,
            new ST14CardCaption("st14-hub-war-mode-galactic-banner", new Vector2(0, 302.5f), 6.56f, ST14HubFont.Black, 28));

        AddCard(1, "card_territory.png", "st14-hub-war-mode-territory", true,
            new ST14CardCaption("st14-hub-war-mode-territory-banner", new Vector2(-1, -336.5f), 6.56f, ST14HubFont.Black, 28),
            new ST14CardCaption("st14-hub-war-in-development", new Vector2(5.8f, 300.2f), -7.4f, ST14HubFont.SemiBold, 24));

        AddCard(2, "card_challenges.png", "st14-hub-war-mode-challenges", true);

        AddBottomBar();
    }

    private void AddCard(int index, string art, string name, bool locked, params ST14CardCaption[] captions)
    {
        var x = (index - 1) * CardStep;

        var card = new ST14ModeCard(ResCache, art, captions)
        {
            Locked = locked,
            Stamp = locked && captions.Length == 0 ? Loc.GetString("st14-hub-war-in-development") : null,
        };
        Canvas.AddAt(card, new Vector2(0.5f, 0.5f), new Vector2(x, CardY), CardSize);

        // the plate hangs over the bottom edge of the card
        var plate = new ST14HubButton(ResCache, Loc.GetString(name), 20, locked ? Color.FromHex("#8A8A8A") : null)
        {
            Disabled = locked,
        };
        Canvas.AddAt(plate, new Vector2(0.5f, 0.5f), new Vector2(x, CardY + CardSize.Y / 2 - 10), new Vector2(400, 70));

        if (locked)
            return;

        card.OnPressed += _ => OnGalacticWar?.Invoke();
        plate.OnPressed += _ => OnGalacticWar?.Invoke();
    }
}

/// <summary>
/// Slanted caption drawn over a banner baked into the card art. Position is from the card centre in canvas units,
/// the angle is the counter clockwise angle of the original
/// </summary>
public readonly record struct ST14CardCaption(string Loc, Vector2 Position, float Angle, ST14HubFont Font, int Size);

/// <summary>
/// Tall mode card: thin frame, the art inside and captions on its banners. Locked cards are dimmed and do not react
/// </summary>
public sealed class ST14ModeCard : ContainerButton
{
    private readonly IResourceCache _cache;
    private readonly (Texture Full, Texture Half) _art;
    private readonly StyleBoxTexture _frame;
    private readonly (string Text, ST14CardCaption Caption)[] _captions;
    private bool _locked;

    public string? Stamp;

    public ST14ModeCard(IResourceCache cache, string art, ST14CardCaption[] captions)
    {
        _cache = cache;
        MuteSounds = true;
        StyleBoxOverride = new StyleBoxEmpty();

        _art = ST14HubStyle.WarTexPair(cache, art);
        _frame = new StyleBoxTexture
        {
            Texture = ST14HubStyle.WarTex(cache, "card_frame.png"),
            TextureScale = new Vector2(0.5f, 0.5f),
        };
        _frame.SetPatchMargin(StyleBox.Margin.All, 8);

        _captions = captions.Select(c => (Loc.GetString(c.Loc).ToUpperInvariant(), c)).ToArray();
        OnPressed += _ => Sounds.Play(ST14HubSound.Click);
    }

    public bool Locked
    {
        get => _locked;
        set
        {
            _locked = value;
            Disabled = value;
            DefaultCursorShape = value ? CursorShape.Arrow : CursorShape.Hand;
        }
    }

    private ST14HubSoundController Sounds => UserInterfaceManager.GetUIController<ST14HubSoundController>();

    protected override void MouseEntered()
    {
        base.MouseEntered();

        if (!Disabled)
            Sounds.Play(ST14HubSound.Hover);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var scale = size.Y / 838.2f;
        var hover = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;

        _frame.Modulate = hover ? ST14HubStyle.GoldLight : Color.White;
        _frame.Draw(handle, UIBox2.FromDimensions(Vector2.Zero, size), scale);

        var tint = _locked ? new Color(0.36f, 0.36f, 0.38f) : hover ? Color.White : new Color(0.88f, 0.88f, 0.88f);
        var inner = new UIBox2(5 * scale, 5 * scale, size.X - 5 * scale, size.Y - 5 * scale);
        handle.DrawTextureRect(ST14HubStyle.Pick(_art, inner.Width), inner, tint);

        var center = size / 2;
        foreach (var (text, caption) in _captions)
        {
            var font = ST14HubStyle.Font(_cache, caption.Font, Math.Max(8, (int) MathF.Round(caption.Size * scale)));
            ST14HubStyle.DrawRotatedText(handle, font, text, center + caption.Position * scale, -caption.Angle,
                _locked ? new Color(0.85f, 0.85f, 0.85f) : Color.White, ST14HubStyle.TextShadow);
        }

        if (Stamp != null)
            ST14ClassifiedStamp.DrawStamp(handle, _cache, Stamp, center, new Vector2(500, 70) * scale, -6.6f, scale);
    }
}

/// <summary>
/// Dark red stamp with a frame and white caption, the "CLASSIFIED" mark of the original
/// </summary>
public static class ST14ClassifiedStamp
{
    public static void DrawStamp(DrawingHandleScreen handle, IResourceCache cache, string text, Vector2 center, Vector2 size,
        float degrees, float scale)
    {
        var baseTransform = handle.GetTransform();
        handle.SetTransform(Matrix3x2.CreateRotation(MathF.PI * degrees / 180f) * Matrix3x2.CreateTranslation(center) * baseTransform);

        var half = size / 2;
        handle.DrawRect(new UIBox2(-half + new Vector2(2.5f * scale), half - new Vector2(2.5f * scale)), new Color(0.12f, 0f, 0f, 0.85f));

        var frame = new Color(0.49f, 0.09f, 0.09f);
        var t = MathF.Max(1f, 3f * scale);
        handle.DrawRect(new UIBox2(-half.X, -half.Y, half.X, -half.Y + t), frame);
        handle.DrawRect(new UIBox2(-half.X, half.Y - t, half.X, half.Y), frame);
        handle.DrawRect(new UIBox2(-half.X, -half.Y, -half.X + t, half.Y), frame);
        handle.DrawRect(new UIBox2(half.X - t, -half.Y, half.X, half.Y), frame);

        var font = ST14HubStyle.Font(cache, ST14HubFont.SemiBold, Math.Max(8, (int) MathF.Round(size.Y * 0.45f)));
        handle.SetTransform(baseTransform);
        ST14HubStyle.DrawRotatedText(handle, font, text.ToUpperInvariant(), center, degrees, Color.White);
    }
}
