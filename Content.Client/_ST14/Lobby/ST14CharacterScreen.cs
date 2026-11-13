using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._ST14.Lobby;

/// <summary>
/// Character editor laid out as a personnel file: the roster on the left, the soldier on a lit pedestal with the name
/// and randomizer in the middle, the tabbed form with the action buttons on the right. The working controls are taken
/// from the stock <see cref="CharacterSetupGui"/> and <see cref="HumanoidProfileEditor"/>, which stay hidden and keep
/// all their logic. Columns get exact rectangles in <see cref="ArrangeOverride"/>
/// </summary>
public sealed class ST14CharacterScreen : Control
{
    private const float HeaderHeight = 50;
    private const float Gap = 12;

    private readonly IResourceCache _cache;
    private readonly BoxContainer _header;
    private readonly ST14LobbyFrame _roster;
    private readonly ST14LobbyFrame _preview;
    private readonly ST14LobbyFrame _form;
    private readonly ScrollContainer _rosterScroll;
    private readonly BoxContainer _previewColumn;
    private readonly BoxContainer _formColumn;

    private CharacterSetupGui? _taken;

    public ST14CharacterScreen(IResourceCache cache)
    {
        _cache = cache;

        _header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        var headerPanel = new PanelContainer { PanelOverride = ST14LobbyStyle.Glass(0.85f) };
        headerPanel.AddChild(_header);
        AddChild(headerPanel);

        var title = ST14HubStyle.Label(cache, Loc.GetString("st14-lobby-editor-title").ToUpperInvariant(), ST14HubFont.SemiBold, 20,
            ST14HubStyle.GoldLight);
        title.VAlign = Label.VAlignMode.Center;
        title.HorizontalExpand = true;
        title.ClipText = true;
        _header.AddChild(title);

        _roster = new ST14LobbyFrame(cache, Loc.GetString("st14-lobby-editor-roster"));
        _rosterScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _roster.Body.AddChild(_rosterScroll);
        AddChild(_roster);

        _preview = new ST14LobbyFrame(cache, Loc.GetString("st14-lobby-editor-soldier"));
        var previewScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _previewColumn = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };
        previewScroll.AddChild(_previewColumn);
        _preview.Body.AddChild(previewScroll);
        AddChild(_preview);

        _form = new ST14LobbyFrame(cache, Loc.GetString("st14-lobby-editor-form"));
        _formColumn = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            VerticalExpand = true,
        };
        _form.Body.AddChild(_formColumn);
        AddChild(_form);
    }

    private static void Move(Control? control, Control parent)
    {
        if (control == null)
            return;

        control.Orphan();
        parent.AddChild(control);
    }

    /// <summary>
    /// Moves the controls of a freshly built stock editor into this screen
    /// </summary>
    public void Take(CharacterSetupGui gui)
    {
        if (_taken == gui)
            return;

        _taken = gui;
        gui.Visible = false;

        // the stock editor is built again on every lobby visit, drop what was moved out of the previous one
        foreach (var child in _header.Children.Skip(1).ToList())
        {
            child.Orphan();
        }

        foreach (var holder in new Control[] { _rosterScroll, _previewColumn, _formColumn })
        {
            foreach (var child in holder.Children.Where(c => c is not ScrollBar).ToList())
            {
                child.Orphan();
            }
        }

        // header: stats, admin remarks, rules, close
        foreach (var name in new[] { "StatsButton", "AdminRemarksButton", "RulesButton", "CloseButton" })
        {
            if (gui.FindControl<Control>(name) is not { } button)
                continue;

            Move(button, _header);
            button.HorizontalExpand = false;
            button.HorizontalAlignment = HAlignment.Right;
            button.VerticalAlignment = VAlignment.Center;
            button.MinHeight = 38;
        }

        // roster of characters with the "new character" button at the end
        var characters = gui.FindControl<BoxContainer>("Characters");
        Move(characters, _rosterScroll);
        characters.SeparationOverride = 6;

        if (gui.FindControl<BoxContainer>("CharEditor").Children.FirstOrDefault() is HumanoidProfileEditor editor)
            TakeEditor(editor);

        // sprite previews drop their dummy entity when they leave the UI tree, so load everything again in the new place
        UserInterfaceManager.DeferAction(() => UserInterfaceManager.GetUIController<LobbyUIController>().ReloadCharacterSetup());
    }

    private void TakeEditor(HumanoidProfileEditor editor)
    {
        // the soldier on the pedestal with the rotate buttons under it
        var sprite = editor.FindControl<ProfilePreviewSpriteView>("SpriteView");
        sprite.Orphan();
        sprite.Scale = new Vector2(6, 6);
        sprite.Margin = new Thickness(0, 0, 0, 18);
        sprite.HorizontalAlignment = HAlignment.Center;
        sprite.VerticalAlignment = VAlignment.Bottom;
        var pedestal = new ST14Pedestal(_cache) { MinSize = new Vector2(0, 250) };
        pedestal.AddChild(sprite);
        _previewColumn.AddChild(pedestal);

        var rotate = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 8,
        };
        _previewColumn.AddChild(rotate);
        foreach (var name in new[] { "SpriteRotateLeft", "SpriteRotateRight" })
        {
            var button = editor.FindControl<Button>(name);
            Move(button, rotate);
            button.MinSize = new Vector2(56, 34);
        }

        // name with the naming rules warning
        var nameLabel = ST14HubStyle.Label(_cache, Loc.GetString("humanoid-profile-editor-name-label"), ST14HubFont.Medium, 14,
            ST14HubStyle.Gold);
        _previewColumn.AddChild(nameLabel);

        var nameEdit = editor.FindControl<LineEdit>("NameEdit");
        Move(nameEdit, _previewColumn);
        nameEdit.MinSize = new Vector2(0, 32);
        nameEdit.HorizontalExpand = true;
        nameEdit.Margin = new Thickness(0);

        // bugs pick a hive prefix and a number instead of typing a name
        var bugName = editor.ST14BugNameRow;
        Move(bugName, _previewColumn);

        var warning = editor.FindControl<RichTextLabel>("WarningLabel");
        Move(warning, _previewColumn);
        warning.MaxWidth = float.PositiveInfinity;
        warning.HorizontalAlignment = HAlignment.Stretch;

        // randomizer with its locks, the whole box moves as one
        var randomize = editor.FindControl<Button>("RandomizeUnlockedButton");
        if (randomize.Parent is BoxContainer randomBox)
        {
            var section = new PanelContainer { PanelOverride = ST14LobbyStyle.Glass(0.6f), Margin = new Thickness(0, 6, 0, 0) };
            Move(randomBox, section);
            _previewColumn.AddChild(section);
            randomize.MaxWidth = float.PositiveInfinity;
            randomize.HorizontalAlignment = HAlignment.Stretch;
            randomize.MinHeight = 34;
            foreach (var grid in randomBox.Children.OfType<GridContainer>())
            {
                grid.MinWidth = 0;
            }
        }

        // tabbed form: every tab already scrolls inside itself
        var tabs = editor.FindControl<TabContainer>("TabContainer");
        Move(tabs, _formColumn);
        tabs.VerticalExpand = true;
        tabs.HorizontalExpand = true;

        // file actions in a fixed grid under the form, three per row so they never run out of room
        var actions = new GridContainer { Columns = 3, HSeparationOverride = 8, VSeparationOverride = 6 };
        _formColumn.AddChild(actions);
        foreach (var name in new[] { "SaveButton", "ResetButton", "ImportButton", "ExportButton", "ExportImageButton", "OpenImagesButton" })
        {
            var button = editor.FindControl<Button>(name);
            Move(button, actions);
            button.HorizontalExpand = true;
            button.MinHeight = 36;
            button.Label.ClipText = true;
        }

        var save = editor.FindControl<Button>("SaveButton");
        save.StyleClasses.Add("ButtonBig");
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
        var w = finalSize.X;
        var h = finalSize.Y;
        var bodyTop = HeaderHeight + Gap;
        var bodyHeight = MathF.Max(0, h - bodyTop);

        var rosterWidth = Math.Clamp(w * 0.2f, 230, 330);
        var previewWidth = Math.Clamp(w * 0.24f, 270, 380);
        var formWidth = w - rosterWidth - previewWidth - Gap * 2;
        if (formWidth < 360)
        {
            // narrow window: the side columns give way to the form
            var k = MathF.Max(0.5f, (w - 360 - Gap * 2) / (rosterWidth + previewWidth));
            rosterWidth *= k;
            previewWidth *= k;
            formWidth = w - rosterWidth - previewWidth - Gap * 2;
        }

        Children.First().Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(w, HeaderHeight)));
        _roster.Arrange(UIBox2.FromDimensions(new Vector2(0, bodyTop), new Vector2(rosterWidth, bodyHeight)));
        _preview.Arrange(UIBox2.FromDimensions(new Vector2(rosterWidth + Gap, bodyTop), new Vector2(previewWidth, bodyHeight)));
        _form.Arrange(UIBox2.FromDimensions(new Vector2(rosterWidth + previewWidth + Gap * 2, bodyTop), new Vector2(formWidth, bodyHeight)));
        return finalSize;
    }
}
