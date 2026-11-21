/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.ZLevels.Fortifications;

/// <summary>
/// The entrenching tool's build window: a trench, a dug-out, steps or a cable trench. Picking one puts its ghost on the
/// cursor (R turns it)
/// </summary>
public sealed class ST14TrenchMenuWindow : DefaultWindow
{
    public event Action<ST14DigMode>? ModeSelected;

    /// <summary>
    /// ST14: the size of a trench segment changed, length and width
    /// </summary>
    public event Action<int, int>? SizeChanged;

    private readonly Label _lengthLabel = new() { MinWidth = 24, HorizontalAlignment = HAlignment.Center };
    private readonly Label _widthLabel = new() { MinWidth = 24, HorizontalAlignment = HAlignment.Center };
    private int _length = 3;
    private int _width = 1;

    private static readonly (ST14DigMode Mode, EntProtoId Icon)[] Entries =
    [
        (ST14DigMode.Trench, "ST14TrenchBlueprintMarker"),
        (ST14DigMode.Dugout, "ST14DugoutRoof"),
        (ST14DigMode.Corner, "ST14DugoutRoof"),
        (ST14DigMode.Door, "ST14TrenchRim"),
        (ST14DigMode.Steps, "ST14TrenchSteps"),
        (ST14DigMode.Fill, "ST14TrenchWall"),
        (ST14DigMode.Cable, "ST14EntrenchingTool"),
    ];

    private readonly List<(ST14DigMode Mode, Button Button)> _buttons = new();

    public ST14TrenchMenuWindow(IEntityManager entMan, ST14TrenchDiggerSystem digger)
    {
        Title = Loc.GetString("st14-trench-menu-title");
        Resizable = false;

        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };

        foreach (var (mode, icon) in Entries)
        {
            var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
            content.AddChild(new EntityPrototypeView(icon, entMan)
            {
                SetSize = new Vector2(48, 48),
                HorizontalAlignment = HAlignment.Center,
            });
            content.AddChild(new Label
            {
                Text = digger.ModeName(mode),
                HorizontalAlignment = HAlignment.Center,
            });

            var button = new Button
            {
                ToggleMode = true,
                MinWidth = 96,
                ToolTip = Loc.GetString($"st14-trench-menu-{mode.ToString().ToLowerInvariant()}-desc"),
            };
            button.AddChild(content);
            button.OnPressed += _ => ModeSelected?.Invoke(mode);

            row.AddChild(button);
            _buttons.Add((mode, button));
        }

        column.AddChild(row);

        // the size of a trench segment: the longer and wider, the longer it takes to dig
        var size = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalAlignment = HAlignment.Center,
        };
        AddStepper(size, "st14-trench-menu-length", _lengthLabel, d => SetTrenchSize(_length + d, _width));
        size.AddChild(new Robust.Client.UserInterface.Control { MinWidth = 24 });
        AddStepper(size, "st14-trench-menu-width", _widthLabel, d => SetTrenchSize(_length, _width + d));
        column.AddChild(size);
        ShowSize();

        column.AddChild(new Label
        {
            Text = Loc.GetString("st14-trench-menu-hint"),
            FontColorOverride = Color.FromHex("#a0a0a0"),
            HorizontalAlignment = HAlignment.Center,
        });

        Contents.AddChild(column);
    }

    private static void AddStepper(BoxContainer row, string loc, Label value, Action<int> step)
    {
        row.AddChild(new Label { Text = Loc.GetString(loc), VerticalAlignment = VAlignment.Center });
        var less = new Button { Text = "−", MinWidth = 28 };
        less.OnPressed += _ => step(-1);
        var more = new Button { Text = "+", MinWidth = 28 };
        more.OnPressed += _ => step(1);
        row.AddChild(less);
        row.AddChild(value);
        row.AddChild(more);
    }

    /// <summary>
    /// The size of a trench segment to show, from the tool (clamped to what a segment can be)
    /// </summary>
    public void SetTrenchSize(int length, int width, bool raise = true)
    {
        length = Math.Clamp(length, 1, ST14TrenchDiggerComponent.MaxTrenchLength);
        width = Math.Clamp(width, 1, ST14TrenchDiggerComponent.MaxTrenchWidth);
        var changed = length != _length || width != _width;
        _length = length;
        _width = width;
        ShowSize();
        if (changed && raise)
            SizeChanged?.Invoke(length, width);
    }

    private void ShowSize()
    {
        _lengthLabel.Text = _length.ToString();
        _widthLabel.Text = _width.ToString();
    }

    public void SetSelected(ST14DigMode? mode)
    {
        foreach (var (entry, button) in _buttons)
        {
            button.Pressed = entry == mode;
        }
    }
}
