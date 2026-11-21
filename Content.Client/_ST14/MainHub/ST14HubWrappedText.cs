using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Text of the hub that breaks into lines at the width of its box instead of running past it. The font follows the
/// canvas like the hub labels do: <see cref="DesignWidth"/> is the width of the box in canvas units
/// </summary>
public sealed class ST14HubWrappedText : Control
{
    private readonly IResourceCache _cache;
    private readonly ST14HubFont _font;
    private readonly int _size;
    private readonly List<string> _lines = new();
    private string _text = string.Empty;
    private float _wrappedAt = -1;
    private int _wrappedSize = -1;

    public Color Color = Color.White;
    public float DesignWidth;

    public ST14HubWrappedText(IResourceCache cache, ST14HubFont font, int size, float designWidth)
    {
        _cache = cache;
        _font = font;
        _size = size;
        DesignWidth = designWidth;
        RectClipContent = true;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            _wrappedAt = -1;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_text.Length == 0 || DesignWidth <= 0 || PixelSize.X <= 0)
            return;

        var scale = PixelSize.X / (DesignWidth * UIScale);
        var size = Math.Max(8, (int) MathF.Round(_size * scale));
        var font = ST14HubStyle.Font(_cache, _font, size);
        if (_wrappedAt != PixelSize.X || _wrappedSize != size)
            Wrap(handle, font, size);

        var line = font.GetLineHeight(UIScale);
        var y = 0f;
        foreach (var text in _lines)
        {
            handle.DrawString(font, new Vector2(0, y), text, UIScale, Color);
            y += line;
        }
    }

    private void Wrap(DrawingHandleScreen handle, Font font, int size)
    {
        _wrappedAt = PixelSize.X;
        _wrappedSize = size;
        _lines.Clear();

        var current = string.Empty;
        foreach (var word in _text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && handle.GetDimensions(font, candidate, UIScale).X > PixelSize.X)
            {
                _lines.Add(current);
                current = word;
                continue;
            }

            current = candidate;
        }

        if (current.Length > 0)
            _lines.Add(current);
    }
}
