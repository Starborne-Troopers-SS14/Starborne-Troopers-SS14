using System.Linq;
using System.Numerics;
using Content.Client.Administration.Managers;
using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Full screen settings like the options menu of the original: animated background per tab, a framed window and categories in the bottom bar.
/// Reuses the regular options tabs so saving works the same
/// </summary>
public sealed partial class ST14SettingsScreen : Control
{
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;
    [Dependency] private IClientAdminManager _adminManager = default!;

    private readonly ST14AnimatedScene _scene;
    private readonly ST14CanvasLayout _canvas;
    private readonly List<SettingsTab> _tabs = new();
    private SettingsTab? _current;
    private Stylesheet? _controlsSheet;
    private readonly SettingsTab _adminTab;

    public event Action? OnOpened;
    public event Action? OnClosed;

    public ST14SettingsScreen()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;

        AddChild(new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black) });

        _scene = new ST14AnimatedScene();
        AddChild(_scene);

        _canvas = new ST14CanvasLayout { ScaleLabels = true };
        AddChild(_canvas);

        _canvas.Add(new PanelContainer { PanelOverride = ST14HubStyle.MainBar(_resCache) },
            new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, new Vector2(0, 93), new Vector2(0.5f, 1));

        // Window rects of the original options menu, the audio one is taller since there are more sliders here
        AddTab(0, "preferences", "options_preferences.png", "ST14HubSettingsPreferences",
            new Vector2(431, -49.4f), new Vector2(800, 715), () => new MiscTab(), t => ((MiscTab) t).Control.ReloadValues());
        AddTab(1, "controls", "options_graphics.png", "ST14HubSettingsControls",
            new Vector2(220.75f, 1), new Vector2(1264.5f, 698), () => new KeyRebindTab(), null);
        AddTab(2, "graphics", "options_controls.png", "ST14HubSettingsGraphics",
            new Vector2(-505, -18.6f), new Vector2(800, 767), () => new GraphicsTab(), t => ((GraphicsTab) t).Control.ReloadValues());
        AddTab(3, "audio", "options_audio.png", "ST14HubSettingsAudio",
            new Vector2(390.5f, 0), new Vector2(850.6f, 650), () => new AudioTab(), t => ((AudioTab) t).Control.ReloadValues());
        AddTab(4, "accessibility", "options_preferences.png", "ST14HubSettingsPreferences",
            new Vector2(431, -49.4f), new Vector2(800, 715), () => new AccessibilityTab(), t => ((AccessibilityTab) t).Control.ReloadValues());
        _adminTab = AddTab(5, "admin", "options_controls.png", "ST14HubSettingsGraphics",
            new Vector2(-505, -18.6f), new Vector2(800, 767), () => new AdminOptionsTab(), t => ((AdminOptionsTab) t).Control.ReloadValues());

        var close = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, "exit.png"), ST14HubStyle.Gold)
        {
            PressSound = ST14HubSound.Back,
        };
        close.OnPressed += _ => Close();
        _canvas.AddAt(close, new Vector2(1, 1), new Vector2(-125.6f, -13.8f), new Vector2(54.4f, 57), new Vector2(1, 1));
    }

    public void Show()
    {
        // the admin tab is only there for admins, like in the stock options window
        _adminTab.Button.Visible = _adminManager.IsAdmin(true);
        if (_current == _adminTab && !_adminTab.Button.Visible)
            _current = null;

        Visible = true;
        Select(_current ?? _tabs[0]);
        OnOpened?.Invoke();
    }


    public void Close()
    {
        Visible = false;
        OnClosed?.Invoke();
    }

    private SettingsTab AddTab(int index, string id, string icon, ProtoId<ST14HubScenePrototype> scene, Vector2 position, Vector2 size,
        Func<Control> factory, Action<Control>? reload)
    {
        var button = new ST14HubCategoryButton(_resCache, Loc.GetString($"st14-hub-settings-tab-{id}"), ST14HubStyle.Tex(_resCache, icon));
        // six categories along the bottom bar, the exit icon keeps the right corner
        _canvas.AddAt(button, new Vector2(0, 1), new Vector2(160 + 285 * index, -42.3f), new Vector2(270, 57));

        var tab = new SettingsTab(id, scene, position, size, button, factory, reload);
        button.OnPressed += _ => Select(tab);
        _tabs.Add(tab);
        return tab;
    }

    private void Select(SettingsTab tab)
    {
        var changed = _current != tab;
        _current = tab;

        foreach (var other in _tabs)
        {
            other.Button.Selected = other == tab;
            if (other.Panel != null)
                other.Panel.Visible = other == tab;
        }

        // Tabs are built on first open, this keeps the hub cheap when nobody opens settings
        if (tab.Panel == null)
        {
            var panel = new PanelContainer
            {
                PanelOverride = ST14HubStyle.Window(_resCache),
                Stylesheet = ControlsSheet(),
            };
            var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

            var title = ST14HubStyle.Label(_resCache, Loc.GetString($"st14-hub-settings-panel-{tab.Id}").ToUpperInvariant(),
                ST14HubFont.SemiBold, 21, ST14HubStyle.GoldLight);
            title.Align = Label.AlignMode.Center;
            title.MinHeight = 36;
            body.AddChild(title);

            tab.Contents = tab.Factory();
            tab.Contents.VerticalExpand = true;
            tab.Contents.HorizontalExpand = true;
            tab.Contents.Margin = new Thickness(0, 10, 0, 0);
            AddScroll(tab.Contents);
            Restyle(tab.Contents, false);
            body.AddChild(tab.Contents);

            panel.AddChild(body);
            tab.Panel = panel;
            _canvas.AddAt(panel, new Vector2(0.5f, 0.5f), tab.Position, tab.Size);
        }

        tab.Panel.Visible = true;
        tab.Reload?.Invoke(tab.Contents!);

        if (changed)
            _scene.SetScene(tab.Scene);
    }

    // Controls of the regular options tabs are tagged with these ids, rules with an id win over every rule of the main stylesheet
    private const string ControlId = "st14Settings";
    private const string DropDownId = "st14SettingsDropDown";
    private const string MainButtonId = "st14SettingsMain";

    /// <summary>
    /// Tags the controls of an options tab so <see cref="ControlsSheet"/> dresses them like the options menu of the original.
    /// Sliders are done with overrides right here
    /// </summary>
    private void Restyle(Control control, bool inButtonRow)
    {
        inButtonRow |= control is OptionsTabControlRow;

        switch (control)
        {
            case Slider slider:
                StyleSlider(slider);
                break;
            case OptionButton option:
                option.StyleIdentifier = DropDownId;
                option.MinHeight = 30;
                option.MinWidth = 200;
                // the drop down takes the rest of the row, long values were cut at 200 px
                option.HorizontalExpand = true;
                break;
            case CheckBox checkBox:
                checkBox.TextureRect.StyleIdentifier = ControlId;
                checkBox.TextureRect.TextureScale = new Vector2(0.21f, 0.21f);
                checkBox.Label.FontColorOverride = ST14HubStyle.Gold;
                checkBox.Label.ClipText = true;
                checkBox.Label.HorizontalExpand = true;
                break;
            case Button button:
                button.StyleIdentifier = inButtonRow ? MainButtonId : ControlId;
                button.Label.FontColorOverride = inButtonRow ? Color.White : ST14HubStyle.GoldLight;
                if (inButtonRow)
                {
                    button.MinHeight = 38;
                    button.Label.FontOverride = ST14HubStyle.Font(_resCache, ST14HubFont.Regular, 13);
                }
                break;
            case TextureRect triangle when triangle.HasStyleClass(OptionButton.StyleClassOptionTriangle):
                triangle.StyleIdentifier = ControlId;
                triangle.Margin = new Thickness(8, 0, 0, 0);
                break;
            case Label label when label.HasStyleClass(OptionButton.StyleClassOptionButton):
                label.FontColorOverride = ST14HubStyle.Gold;
                label.Align = Label.AlignMode.Left;
                label.ClipText = true;
                break;
            // name column of the option rows is 400 px wide, too much for the windows of the original
            // a fixed column, so the sliders and drop downs of every row start at the same x; a cut name shows in full
            // on hover
            case Label { MinWidth: 400 } rowName:
                rowName.MinWidth = 300;
                rowName.SetWidth = 300;
                rowName.ClipText = true;
                rowName.Margin = new Thickness(0, 0, 12, 0);
                rowName.ToolTip = rowName.Text;
                rowName.MouseFilter = MouseFilterMode.Pass;
                break;
            case ScrollContainer scroll:
                scroll.HScrollEnabled = false;
                break;
            case ScrollBar scrollBar:
                scrollBar.StyleIdentifier = ControlId;
                break;
        }

        foreach (var child in control.Children)
        {
            Restyle(child, inButtonRow);
        }
    }

    /// <summary>
    /// Tabs without their own scroll (audio) grow past the window, put everything above the button row into a scroll box
    /// </summary>
    private static void AddScroll(Control contents)
    {
        var row = FindRow(contents);
        if (row?.Parent is not { } box || box.Children.Any(c => c != row && HasScroll(c)))
            return;

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        scroll.AddChild(column);
        foreach (var child in box.Children.Where(c => c != row).ToList())
        {
            box.RemoveChild(child);
            column.AddChild(child);
        }

        box.AddChild(scroll);
        scroll.SetPositionInParent(0);
    }

    private static OptionsTabControlRow? FindRow(Control control)
    {
        if (control is OptionsTabControlRow row)
            return row;

        foreach (var child in control.Children)
        {
            if (FindRow(child) is { } found)
                return found;
        }

        return null;
    }

    private static bool HasScroll(Control control)
    {
        return control is ScrollContainer || control.Children.Any(HasScroll);
    }

    /// <summary>
    /// Sliders of the original: a dark textured groove, a flat gold fill set in from its edges and a thin light handle
    /// </summary>
    private void StyleSlider(Slider slider)
    {
        var groove = ST14HubStyle.Patch(_resCache, "editor_button.png", 8, 9, 9, 8, new Color(0.6f, 0.6f, 0.6f));
        groove.SetContentMarginOverride(StyleBox.Margin.All, 0);
        slider.BackgroundStyleBoxOverride = groove;
        slider.ForegroundStyleBoxOverride = new StyleBoxEmpty();

        var fill = ST14HubStyle.Patch(_resCache, "slider_fill.png", 8, 8, 8, 8);
        fill.SetContentMarginOverride(StyleBox.Margin.All, 0);
        slider.FillStyleBoxOverride = fill;

        var handle = ST14HubStyle.Flat(Color.FromHex("#F5F5F5C8"));
        handle.SetContentMarginOverride(StyleBox.Margin.Horizontal, 1.5f);
        slider.GrabberStyleBoxOverride = handle;
        slider.MinHeight = 22;
    }

    /// <summary>
    /// Main stylesheet plus the rules for the tagged controls: beveled key plates, square check boxes with a grey tick,
    /// black drop downs with a chevron, a thin gold scroll bar and metal plates for the buttons along the bottom
    /// </summary>
    private Stylesheet ControlsSheet()
    {
        if (_controlsSheet != null)
            return _controlsSheet;

        var key = ST14HubStyle.Patch(_resCache, "unitbutton.png", 26, 26, 26, 26);
        key.TextureScale = new Vector2(0.3f, 0.3f);
        key.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        key.SetContentMarginOverride(StyleBox.Margin.Vertical, 3);

        var plate = ST14HubStyle.Patch(_resCache, "main_button.png", 16, 16, 72, 72);
        plate.TextureScale = new Vector2(0.3f, 0.3f);
        // the right edge of the plate is the cut corner, keep the text off it
        plate.SetContentMarginOverride(StyleBox.Margin.Left, 10);
        plate.SetContentMarginOverride(StyleBox.Margin.Right, 22);
        plate.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);

        var drop = ST14HubStyle.Flat(Color.Black, ST14HubStyle.Gold.WithAlpha(0.35f), 1);
        drop.SetContentMarginOverride(StyleBox.Margin.Horizontal, 10);
        drop.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);

        StyleBoxFlat Grabber(Color color)
        {
            var box = ST14HubStyle.Flat(color);
            box.ContentMarginLeftOverride = 6;
            box.ContentMarginTopOverride = 10;
            return box;
        }

        var dim = new Color(0.82f, 0.82f, 0.82f);
        var disabled = Color.White.WithAlpha(0.45f);
        var rules = new List<StyleRule>(_uiManager.Stylesheet?.Rules ?? Array.Empty<StyleRule>());
        foreach (var (id, box) in new (string, StyleBox)[] { (ControlId, key), (MainButtonId, plate), (DropDownId, drop) })
        {
            rules.Add(E<ContainerButton>().Identifier(id).Box(box));
            rules.Add(E<ContainerButton>().Identifier(id).PseudoNormal().Prop(Control.StylePropertyModulateSelf, dim));
            rules.Add(E<ContainerButton>().Identifier(id).PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White));
            rules.Add(E<ContainerButton>().Identifier(id).PseudoPressed().Prop(Control.StylePropertyModulateSelf, ST14HubStyle.GoldLight));
            rules.Add(E<ContainerButton>().Identifier(id).PseudoDisabled().Prop(Control.StylePropertyModulateSelf, disabled));
        }

        rules.Add(E<TextureRect>().Identifier(ControlId).Class(CheckBox.StyleClassCheckBox)
            .Prop(TextureRect.StylePropertyTexture, ST14HubStyle.Tex(_resCache, "checkbox_off.png")));
        rules.Add(E<TextureRect>().Identifier(ControlId).Class(CheckBox.StyleClassCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
            .Prop(TextureRect.StylePropertyTexture, ST14HubStyle.Tex(_resCache, "checkbox_on.png")));
        rules.Add(E<TextureRect>().Identifier(ControlId).Class(OptionButton.StyleClassOptionTriangle)
            .Prop(TextureRect.StylePropertyTexture, ST14HubStyle.Tex(_resCache, "dropdownarrow.png"))
            .Modulate(ST14HubStyle.Gold));

        rules.Add(E<VScrollBar>().Identifier(ControlId).Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.Gold.WithAlpha(0.55f))));
        rules.Add(E<VScrollBar>().Identifier(ControlId).PseudoHovered()
            .Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.Gold.WithAlpha(0.8f))));
        rules.Add(E<VScrollBar>().Identifier(ControlId).PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.GoldLight)));

        _controlsSheet = new Stylesheet(rules);
        return _controlsSheet;
    }

    private sealed class SettingsTab(
        string id,
        ProtoId<ST14HubScenePrototype> scene,
        Vector2 position,
        Vector2 size,
        ST14HubCategoryButton button,
        Func<Control> factory,
        Action<Control>? reload)
    {
        public readonly string Id = id;
        public readonly ProtoId<ST14HubScenePrototype> Scene = scene;
        public readonly Vector2 Position = position;
        public readonly Vector2 Size = size;
        public readonly ST14HubCategoryButton Button = button;
        public readonly Func<Control> Factory = factory;
        public readonly Action<Control>? Reload = reload;
        public PanelContainer? Panel;
        public Control? Contents;
    }
}

/// <summary>
/// Square icon plate with a caption, the category switch in the bottom bar of the settings
/// </summary>
public sealed class ST14HubCategoryButton : ContainerButton
{
    private readonly IResourceCache _cache;
    private readonly PanelContainer _plate;
    private readonly TextureRect _icon;
    private readonly Label _label;
    private bool _selected;

    public ST14HubCategoryButton(IResourceCache cache, string text, Texture icon)
    {
        _cache = cache;
        MuteSounds = true;

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        _plate = new PanelContainer { SetSize = new Vector2(57, 57) };
        _icon = new TextureRect
        {
            Texture = icon,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Margin = new Thickness(6),
        };
        _plate.AddChild(_icon);
        row.AddChild(_plate);

        _label = ST14HubStyle.Label(cache, text.ToUpperInvariant(), ST14HubFont.Regular, 19, ST14HubStyle.Gold, shadow: true);
        _label.VAlign = Label.VAlignMode.Center;
        _label.HorizontalExpand = true;
        _label.Align = Label.AlignMode.Center;
        row.AddChild(_label);

        AddChild(row);
        OnPressed += _ => Sounds.Play(ST14HubSound.Tab);
        UpdateLook();
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            UpdateLook();
        }
    }

    private ST14HubSoundController Sounds => UserInterfaceManager.GetUIController<ST14HubSoundController>();

    protected override void MouseEntered()
    {
        base.MouseEntered();
        Sounds.Play(ST14HubSound.Hover);
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateLook();
    }

    private void UpdateLook()
    {
        if (_label == null)
            return;

        var lit = _selected || DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        _plate.PanelOverride = ST14HubStyle.UnitButton(_cache, lit ? Color.White : Color.FromHex("#B8B8B8"));
        _icon.ModulateSelfOverride = lit ? Color.White : Color.FromHex("#C8C8C8");
        _label.FontColorOverride = lit ? ST14HubStyle.GoldLight : ST14HubStyle.Gold;
    }
}
