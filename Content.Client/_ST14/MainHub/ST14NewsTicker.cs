using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// News line of the hub banner: all news one after another, gliding from right to left without stops.
/// The offset is kept in canvas units so the speed and the text keep their size when the window is resized
/// </summary>
public sealed class ST14NewsTicker : Control
{
    // canvas units per second and the gap between two news items
    private const float Speed = 110f;
    private const string Separator = "     ///     ";

    private readonly IResourceCache _cache;
    private readonly int _size;
    private string _text = string.Empty;
    private float _offset;
    private float _width;

    public Color Color = Color.White;

    public ST14NewsTicker(IResourceCache cache, int size)
    {
        _cache = cache;
        _size = size;
        RectClipContent = true;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value + Separator;
            _offset = 0;
            _width = 0;
        }
    }

    // the canvas scale, the banner is 50 canvas units high; fonts get the UI scale on top like labels do
    private float Scale => PixelSize.Y / (50f * UIScale);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!VisibleInTree)
            return;

        _offset += Speed * args.DeltaSeconds;
        if (_width > 0 && _offset >= _width)
            _offset -= _width;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = Scale;
        if (_text.Length == 0 || scale <= 0)
            return;

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Regular, Math.Max(8, (int) MathF.Round(_size * scale)));
        var dims = handle.GetDimensions(font, _text, UIScale);
        if (dims.X <= 0)
            return;

        // one copy of the text in canvas units, the loop restarts after it
        var pixels = scale * UIScale;
        _width = dims.X / pixels;

        var y = (PixelSize.Y - dims.Y) / 2;
        var x = -_offset * pixels;
        while (x < PixelSize.X)
        {
            handle.DrawString(font, new Vector2(x, y), _text, UIScale, Color);
            x += dims.X;
        }
    }
}
