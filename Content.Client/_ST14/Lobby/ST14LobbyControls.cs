using System.Numerics;
using Content.Client._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Lobby;

/// <summary>
/// Holds one child and lets it slide in from an offset while fading in, eased out like the panels of the original menu
/// </summary>
public sealed class ST14SlideIn : Control
{
    private Vector2 _from;
    private float _duration = 1f;
    private float _time = 1f;
    private float _delay;

    public ST14SlideIn()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void Play(Vector2 from, float duration = 0.45f, float delay = 0f)
    {
        _from = from;
        _duration = duration;
        _delay = delay;
        _time = 0f;
        Modulate = Color.White.WithAlpha(0f);
        InvalidateArrange();
    }

    private float Progress
    {
        get
        {
            var t = Math.Clamp((_time - _delay) / _duration, 0f, 1f);
            return 1f - MathF.Pow(1f - t, 3f);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_time >= _delay + _duration)
            return;

        _time += args.DeltaSeconds;
        Modulate = Color.White.WithAlpha(Progress);
        InvalidateArrange();
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var offset = _from * (1f - Progress);
        foreach (var child in Children)
        {
            child.Arrange(UIBox2.FromDimensions(offset, finalSize));
        }

        return finalSize;
    }
}

/// <summary>
/// Window of the original menu: dark glass with a brass frame and the title on the angled header strip
/// </summary>
public sealed class ST14LobbyFrame : PanelContainer
{
    public readonly BoxContainer Body;
    public readonly Label Title;

    public ST14LobbyFrame(IResourceCache cache, string title)
    {
        PanelOverride = ST14HubStyle.Window(cache);

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        Title = ST14HubStyle.Label(cache, title.ToUpperInvariant(), ST14HubFont.SemiBold, 17, ST14HubStyle.GoldLight);
        Title.Align = Label.AlignMode.Center;
        Title.MinHeight = 36;
        Title.ClipText = true;
        column.AddChild(Title);

        Body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            SeparationOverride = 6,
            Margin = new Thickness(2, 8, 2, 0),
        };
        column.AddChild(Body);
        AddChild(column);
    }
}

/// <summary>
/// Lit stand for a character preview: a warm glow behind the soldier and a disc with a light rim under the feet
/// </summary>
public sealed class ST14Pedestal : Control
{
    private readonly Texture _glow;
    private readonly Texture _disc;
    private float _time;

    public Color GlowColor = ST14HubStyle.GoldLight;
    public Color RimColor = new(0.36f, 0.75f, 0.9f);

    public ST14Pedestal(IResourceCache cache)
    {
        _glow = cache.GetResource<TextureResource>("/Textures/_ST14/Interface/Lobby/soft_glow.png").Texture;
        _disc = cache.GetResource<TextureResource>("/Textures/_ST14/Interface/Lobby/pedestal.png").Texture;
        Margin = new Thickness(0, 0, 0, 6);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        // slow breathing light, a few percent so it never draws attention away from the character
        var pulse = 0.85f + 0.15f * MathF.Sin(_time * MathF.Tau / 4f);

        // the glow stays inside the stand and centred on the soldier: a frame around the stand would cut it off
        var glowSize = new Vector2(size.X, size.Y);
        var glowPos = Vector2.Zero;
        handle.DrawTextureRect(_glow, UIBox2.FromDimensions(glowPos, glowSize), GlowColor.WithAlpha(0.22f * pulse));

        var discSize = new Vector2(size.X * 0.82f, size.X * 0.82f * 0.25f);
        var discPos = new Vector2((size.X - discSize.X) / 2, size.Y - discSize.Y * 0.75f);
        handle.DrawTextureRect(_glow, UIBox2.FromDimensions(discPos - discSize * 0.2f, discSize * 1.4f), RimColor.WithAlpha(0.35f * pulse));
        handle.DrawTextureRect(_disc, UIBox2.FromDimensions(discPos, discSize), Color.White.WithAlpha(0.9f));
        handle.DrawTextureRect(_disc, UIBox2.FromDimensions(discPos, discSize), RimColor.WithAlpha(0.35f * pulse));
    }
}
