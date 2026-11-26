using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Inventory.Controls;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Hud;

/// <summary>
/// Clothing slots of the player laid out over the hands instead of the stock grid: two rows bent into an arch.
/// The lower row holds helmet, armour, uniform, boots and gloves, the upper one the small things, each right over
/// its column of the lower row. Opening slides every slot out of the hands one after another, closing pulls them back.
/// The bottom edge of the control is the middle of the hands, so it is placed right on top of them.
/// </summary>
public sealed class ST14FanSlotContainer : ItemSlotButtonContainer
{
    private const float Gap = 12f;
    private const float Duration = 0.18f;
    private const float Stagger = 0.014f;

    // how much higher the middle column stands than its neighbour, per step from the edge, squared
    private const float Arch = 9f;

    private static readonly string[] InnerOrder = new[] { "head", "outerClothing", "jumpsuit", "shoes", "gloves" };

    private static readonly string[] OuterOrder =
        new[] { "mask", "eyes", "ears", "neck", "id", "capsule" };

    private bool _open;
    private float _time;

    private readonly List<(SlotControl Slot, Vector2 Center)> _layout = new();
    private Vector2 _size;

    public bool Open => _open;

    public ST14FanSlotContainer()
    {
        Visible = false;
        // the slots coming out of the hands are cut at the bottom edge
        RectClipContent = true;
    }

    public void SetOpen(bool open)
    {
        _open = open;
        if (open)
            Visible = true;
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);

        // hidden from outside, e.g. when the game state is left: start closed next time
        if (!newVisible)
        {
            _open = false;
            _time = 0;
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var total = Duration + Stagger * Math.Max(0, _layout.Count - 1);
        var target = _open ? total : 0f;
        if (MathHelper.CloseTo(_time, target))
            return;

        _time = _open
            ? Math.Min(target, _time + args.DeltaSeconds)
            : Math.Max(target, _time - args.DeltaSeconds);

        InvalidateArrange();

        if (!_open && _time <= 0)
            Visible = false;
    }

    private void BuildLayout()
    {
        _layout.Clear();

        var slots = Children.OfType<SlotControl>().ToList();
        var inner = slots.Where(s => InnerOrder.Contains(s.SlotName))
            .OrderBy(s => Array.IndexOf(InnerOrder, s.SlotName)).ToList();
        var outer = slots.Except(inner)
            .OrderBy(s => Array.IndexOf(OuterOrder, s.SlotName) is var i and >= 0 ? i : OuterOrder.Length)
            .ThenBy(s => s.SlotName).ToList();

        // with only a few slots everything fits in the lower row
        if (inner.Count == 0 || outer.Count == 0)
        {
            inner = inner.Concat(outer).ToList();
            outer.Clear();
        }

        var size = SlotControl.DefaultButtonSize;
        var edge = (Math.Max(inner.Count, outer.Count) - 1) / 2f;

        Place(inner, 0, edge);
        Place(outer, 1, edge);

        if (_layout.Count == 0)
        {
            _size = Vector2.Zero;
            return;
        }

        // rows go up from (0, 0), the middle of the hands; the lowest slot must stay above them
        var half = size / 2f;
        var lowest = _layout.Max(p => p.Center.Y) + half + Gap;
        var top = _layout.Min(p => p.Center.Y) - half;
        var right = _layout.Max(p => MathF.Abs(p.Center.X)) + half;

        for (var i = 0; i < _layout.Count; i++)
        {
            var (slot, center) = _layout[i];
            _layout[i] = (slot, center - new Vector2(0, lowest));
        }

        _size = new Vector2(right * 2, lowest - top);
    }

    /// <summary>
    /// Lays the slots left to right in one row, columns one slot and a gap apart, the middle raised into an arch
    /// </summary>
    private void Place(List<SlotControl> slots, int row, float edge)
    {
        var size = SlotControl.DefaultButtonSize;
        for (var i = 0; i < slots.Count; i++)
        {
            var column = i - (slots.Count - 1) / 2f;
            var lift = (edge * edge - column * column) * Arch;
            var x = column * (size + Gap);
            // the lowest slots stand one gap over the hands, the middle of the hands is (0, 0)
            var y = -(size / 2f + Gap + row * (size + Gap) + lift);
            _layout.Add((slots[i], new Vector2(x, y)));
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Vector2(SlotControl.DefaultButtonSize, SlotControl.DefaultButtonSize));
        }

        BuildLayout();
        return _size;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        // the stock controller leaves spacer controls between the slots, they take no room here
        foreach (var child in Children)
        {
            if (child is not SlotControl)
                child.Arrange(UIBox2.FromDimensions(Vector2.Zero, Vector2.Zero));
        }

        var size = SlotControl.DefaultButtonSize;
        var origin = new Vector2(finalSize.X / 2, finalSize.Y);
        var half = new Vector2(size / 2f, size / 2f);

        for (var i = 0; i < _layout.Count; i++)
        {
            var (slot, center) = _layout[i];
            var progress = Math.Clamp((_time - i * Stagger) / Duration, 0f, 1f);
            // ease out: fast start, soft stop
            var eased = 1f - MathF.Pow(1f - progress, 3f);

            var target = origin + center;
            var position = Vector2.Lerp(origin + new Vector2(0, half.Y), target, eased) - half;
            slot.Arrange(UIBox2.FromDimensions(position, new Vector2(size, size)));
            slot.Modulate = Color.White.WithAlpha(eased);
        }

        return finalSize;
    }
}
