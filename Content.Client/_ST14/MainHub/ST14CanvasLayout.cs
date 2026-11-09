using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Places children like Unity rect transforms on a 1920x1080 canvas: anchors on this control, pivot, position and size delta.
/// Positions and sizes scale with the window so the hub keeps the proportions of the original menu
/// </summary>
public sealed class ST14CanvasLayout : Control
{
    public static readonly Vector2 CanvasSize = new(1920, 1080);

    private readonly Dictionary<Control, Slot> _slots = new();

    /// <summary>
    /// Scale fonts of hub labels together with the canvas, set on the top level layout only
    /// </summary>
    public bool ScaleLabels;

    public ST14CanvasLayout()
    {
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    /// <summary>
    /// Scale from canvas units to this control, the canvas always fits inside
    /// </summary>
    public float CanvasScale => MathF.Min(Size.X / CanvasSize.X, Size.Y / CanvasSize.Y);

    public T Add<T>(T control, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        where T : Control
    {
        _slots[control] = new Slot(anchorMin, anchorMax, position, size, pivot ?? new Vector2(0.5f, 0.5f));
        AddChild(control);
        return control;
    }

    /// <summary>
    /// Shortcut for a control anchored to a single point of the screen
    /// </summary>
    public T AddAt<T>(T control, Vector2 anchor, Vector2 position, Vector2 size, Vector2? pivot = null) where T : Control
    {
        return Add(control, anchor, anchor, position, size, pivot);
    }

    /// <summary>
    /// Stretches the control over the whole layout
    /// </summary>
    public T AddFull<T>(T control) where T : Control
    {
        return Add(control, Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.Zero);
    }

    /// <summary>
    /// Moves a child that is already placed, its anchors and size stay
    /// </summary>
    public void MoveTo(Control control, Vector2 position)
    {
        if (!_slots.TryGetValue(control, out var slot) || slot.Position == position)
            return;

        _slots[control] = slot with { Position = position };
        InvalidateArrange();
    }

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);
        _slots.Remove(child);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(availableSize);
        }

        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var scale = MathF.Min(finalSize.X / CanvasSize.X, finalSize.Y / CanvasSize.Y);

        // A layout inside another one, like the content of a card, uses the scale of the screen canvas
        for (var parent = Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ST14CanvasLayout outer)
            {
                scale = outer.CanvasScale;
                break;
            }
        }

        if (ScaleLabels && scale > 0)
            ST14HubStyle.ScaleLabels(this, scale);

        foreach (var child in Children)
        {
            if (!_slots.TryGetValue(child, out var slot))
            {
                child.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
                continue;
            }

            var min = finalSize * slot.AnchorMin;
            var max = finalSize * slot.AnchorMax;
            var size = Vector2.Max(Vector2.Zero, max - min + slot.Size * scale);
            var pivotPos = Vector2.Lerp(min, max, slot.Pivot) + slot.Position * scale;
            child.Arrange(UIBox2.FromDimensions(pivotPos - size * slot.Pivot, size));
        }

        return finalSize;
    }

    private readonly record struct Slot(Vector2 AnchorMin, Vector2 AnchorMax, Vector2 Position, Vector2 Size, Vector2 Pivot);
}
