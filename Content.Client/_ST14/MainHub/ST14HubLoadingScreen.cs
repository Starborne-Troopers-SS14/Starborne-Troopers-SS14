using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Loading screen of the original shown between the hub and the lobby: background art, a tip with a portrait and the stand by bar
/// </summary>
public sealed partial class ST14HubLoadingScreen : Control
{
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const int TipCount = 6;
    private const float FadeTime = 0.25f;

    /// <summary>
    /// Shortest time the screen stays, so it never just blinks
    /// </summary>
    private const float MinTime = 1.2f;

    private readonly ST14HubProgressBar _bar;
    private readonly ST14HubWrappedText _tip;
    private Action? _then;
    private float _elapsed;
    private bool _done;

    public ST14HubLoadingScreen()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
        Visible = false;

        AddChild(new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black) });
        AddChild(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "loading_background.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCovered,
        });

        var canvas = new ST14CanvasLayout { ScaleLabels = true };
        AddChild(canvas);

        // tip block above the bar
        var tipBox = new ST14CanvasLayout();
        tipBox.AddAt(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "fade_lr.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = Color.Black,
        }, new Vector2(0, 1), new Vector2(150, -2.5f), new Vector2(850, 150), new Vector2(0, 1));
        tipBox.AddAt(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "loading_portrait.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCovered,
            RectClipContent = true,
        }, new Vector2(0, 1), new Vector2(6, -8.5f), new Vector2(138, 138), new Vector2(0, 1));
        tipBox.AddAt(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "portrait_frame.png"),
            Stretch = TextureRect.StretchMode.Scale,
        }, new Vector2(0, 1), new Vector2(0, -2.5f), new Vector2(150, 150), new Vector2(0, 1));

        var title = ST14HubStyle.Label(_resCache, Loc.GetString("st14-hub-loading-tip-title"), ST14HubFont.Medium, 15, Color.White.WithAlpha(0.83f));
        tipBox.AddAt(title, new Vector2(1, 0), new Vector2(-10, 0), new Vector2(830, 24), new Vector2(1, 0));

        // a long tip breaks into lines inside the block instead of running off to the right
        _tip = new ST14HubWrappedText(_resCache, ST14HubFont.Regular, 16, 830) { Color = Color.FromHex("#B3B3B3D4") };
        tipBox.AddAt(_tip, new Vector2(1, 0), new Vector2(-10, 30), new Vector2(830, 115), new Vector2(1, 0));
        canvas.AddAt(tipBox, new Vector2(0.5f, 1), new Vector2(0, -170.5f), new Vector2(1000, 150), new Vector2(0.5f, 1));

        // stand by bar
        var barBox = new ST14CanvasLayout();
        barBox.AddFull(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "loading_back.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = Color.FromHex("#2B405463"),
        });
        _bar = new ST14HubProgressBar(Color.FromHex("#21669663"));
        barBox.Add(_bar, Vector2.Zero, Vector2.One, Vector2.Zero, new Vector2(0, -10));
        var standBy = ST14HubStyle.Label(_resCache, Loc.GetString("st14-hub-loading-stand-by"), ST14HubFont.Medium, 15, Color.White.WithAlpha(0.83f));
        standBy.Align = Label.AlignMode.Center;
        standBy.VAlign = Label.VAlignMode.Center;
        barBox.AddFull(standBy);
        canvas.AddAt(barBox, new Vector2(0.5f, 1), new Vector2(0, -111), new Vector2(1000, 32), new Vector2(0.5f, 1));
    }

    /// <summary>
    /// Shows the screen and runs <paramref name="then"/> once the bar is full
    /// </summary>
    public void Start(Action then)
    {
        _then = then;
        _elapsed = 0;
        _done = false;
        _bar.Progress = 0;
        _tip.Text = Loc.GetString($"st14-hub-loading-tip-{_random.Next(TipCount) + 1}");
        Modulate = Color.White.WithAlpha(0);
        Visible = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!Visible || _done)
            return;

        _elapsed += args.DeltaSeconds;
        Modulate = Color.White.WithAlpha(Math.Clamp(_elapsed / FadeTime, 0f, 1f));

        // eased fill, slows down towards the end like a real load
        var t = Math.Clamp(_elapsed / MinTime, 0f, 1f);
        _bar.Progress = 1f - (1f - t) * (1f - t);

        if (_elapsed < MinTime)
            return;

        _done = true;
        var then = _then;
        _then = null;

        // the switch replaces the whole hub, so it must not run while the UI tree is being updated
        if (then != null)
            UserInterfaceManager.DeferAction(then);
    }
}

/// <summary>
/// Flat fill growing from the left
/// </summary>
public sealed class ST14HubProgressBar : Control
{
    private readonly Color _color;

    public float Progress;

    public ST14HubProgressBar(Color color)
    {
        _color = color;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(new UIBox2(0, 0, PixelSize.X * Math.Clamp(Progress, 0f, 1f), PixelSize.Y), _color);
    }
}
