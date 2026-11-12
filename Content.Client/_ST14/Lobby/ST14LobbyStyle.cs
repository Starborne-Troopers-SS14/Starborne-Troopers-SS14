using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._ST14.Lobby;

/// <summary>
/// Stylesheet of the lobby and the character editor: the main stylesheet plus rules in the look of the Terran Command menu.
/// The extra rules repeat the selectors of the main sheet and come after it, so they win at the same specificity
/// and also reach controls that are created later, like the character list or the job rows
/// </summary>
public static class ST14LobbyStyle
{
    public static readonly Color Text = Color.FromHex("#E6DCC0");
    public static readonly Color Selected = Color.FromHex("#5C94D4");
    public static readonly Color Field = Color.FromHex("#0E0D0BF0");

    private static Stylesheet? _base;
    private static Stylesheet? _sheet;

    public static Stylesheet Get(IResourceCache cache, Stylesheet baseSheet)
    {
        if (_sheet != null && _base == baseSheet)
            return _sheet;

        _base = baseSheet;
        _sheet = new Stylesheet(baseSheet.Rules.Concat(Rules(cache)).ToArray());
        return _sheet;
    }

    /// <summary>
    /// Dark glass panel with a thin gold edge, the body of every lobby frame
    /// </summary>
    public static StyleBoxFlat Glass(float alpha = 0.88f)
    {
        var box = ST14HubStyle.Flat(Color.FromHex("#100F0C").WithAlpha(alpha), ST14HubStyle.Gold.WithAlpha(0.45f), 1);
        box.SetContentMarginOverride(StyleBox.Margin.All, 8);
        return box;
    }

    public static StyleBoxTexture Plate(IResourceCache cache)
    {
        // dark beveled plate of the original unit buttons
        var box = ST14HubStyle.Patch(cache, "unitbutton.png", 26, 26, 26, 26);
        box.TextureScale = new Vector2(0.3f, 0.3f);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 3);
        return box;
    }

    private static StyleBoxTexture BigPlate(IResourceCache cache)
    {
        // metal plate with the cut corner, the big buttons of the menu
        var box = ST14HubStyle.Patch(cache, "main_button.png", 16, 16, 72, 72);
        box.TextureScale = new Vector2(0.35f, 0.35f);
        box.SetContentMarginOverride(StyleBox.Margin.Left, 12);
        box.SetContentMarginOverride(StyleBox.Margin.Right, 26);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 6);
        return box;
    }

    /// <summary>
    /// Tab plate of our own two-tab switches: lit gold when selected, dark otherwise
    /// </summary>
    public static StyleBoxTexture Tab(IResourceCache cache, bool selected)
    {
        if (!selected)
        {
            var dark = Plate(cache);
            dark.Modulate = new Color(0.75f, 0.75f, 0.75f);
            return dark;
        }

        var lit = ST14HubStyle.Patch(cache, "main_button.png", 16, 16, 72, 72, ST14HubStyle.GoldLight);
        lit.TextureScale = new Vector2(0.3f, 0.3f);
        lit.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9);
        lit.SetContentMarginOverride(StyleBox.Margin.Vertical, 3);
        return lit;
    }

    /// <summary>
    /// Rules for every window of the game: DefaultWindow and FancyWindow get dark glass with a brass edge and a header
    /// plate with a gold line, alert windows (AHelp, votes) keep a dark red header so they still stand out
    /// </summary>
    public static IEnumerable<StyleRule> WindowRules(IResourceCache cache)
    {
        var body = ST14HubStyle.Flat(Color.FromHex("#12110EF2"), ST14HubStyle.Gold.WithAlpha(0.55f), 1.5f);
        body.SetContentMarginOverride(StyleBox.Margin.All, 0);
        yield return E<PanelContainer>().Class(DefaultWindow.StyleClassWindowPanel).Panel(body);
        yield return E<PanelContainer>().Class(StyleClass.BorderedWindowPanel).Panel(body);

        yield return E<PanelContainer>().Class(DefaultWindow.StyleClassWindowHeader).Panel(Header(Color.FromHex("#2A251BFF")));
        yield return E<PanelContainer>().Class(StyleClass.AlertWindowHeader).Panel(Header(Color.FromHex("#4A1A14FF")));
        yield return E<PanelContainer>().Class("WindowHeadingBackground").Panel(Header(Color.FromHex("#2A251BFF")));
        yield return E<PanelContainer>().Class("WindowHeadingBackgroundLight").Panel(Header(Color.FromHex("#3A3326FF")));

        var titleFont = ST14HubStyle.Font(cache, ST14HubFont.SemiBold, 14);
        yield return E<Label>().Class("windowTitleAlert").Font(titleFont).FontColor(ST14HubStyle.GoldLight);
    }

    private static StyleBoxFlat Header(Color color)
    {
        var box = ST14HubStyle.Flat(color);
        box.BorderColor = ST14HubStyle.Gold.WithAlpha(0.7f);
        box.BorderThickness = new Thickness(0, 0, 0, 1.5f);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        return box;
    }

    /// <summary>
    /// Controls in the look of the Terran Command menu, added after the rules of the main stylesheet
    /// </summary>
    public static IEnumerable<StyleRule> Rules(IResourceCache cache)
    {
        var plate = Plate(cache);
        var big = BigPlate(cache);
        var bigFont = ST14HubStyle.Font(cache, ST14HubFont.Regular, 16);
        var headingFont = ST14HubStyle.Font(cache, ST14HubFont.SemiBold, 15);
        var headingBigFont = ST14HubStyle.Font(cache, ST14HubFont.SemiBold, 19);

        var dim = new Color(0.84f, 0.84f, 0.84f);
        var disabled = new Color(0.55f, 0.55f, 0.55f, 0.6f);

        // every kind of plain button gets the beveled plate, the open side variants included
        string?[] shapes = [null, StyleClass.ButtonOpenLeft, StyleClass.ButtonOpenRight, StyleClass.ButtonOpenBoth, StyleClass.ButtonSquare];
        foreach (var shape in shapes)
        {
            yield return Button().MaybeClass(shape).Box(plate);
        }

        yield return Button().Class(StyleClass.ButtonSmall).Box(plate);
        yield return Button().Class(StyleClass.ButtonBig).Box(big);
        yield return Button().ParentOf(E<Label>()).FontColor(ST14HubStyle.GoldLight);
        yield return Button().Class(StyleClass.ButtonBig).ParentOf(E<Label>()).Font(bigFont).FontColor(Color.White);

        // normal, hover, toggled on (blue like the selected plates of the original), disabled
        yield return Button().PseudoNormal().Prop(Control.StylePropertyModulateSelf, dim);
        yield return Button().PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.White);
        yield return Button().PseudoPressed().Prop(Control.StylePropertyModulateSelf, Selected);
        yield return Button().PseudoDisabled().Prop(Control.StylePropertyModulateSelf, disabled);

        // rows of lists (AHelp players, admin lists): dark rows, the selected one dark gold with readable text
        var row = ST14HubStyle.Flat(Color.White);
        row.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4);
        row.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        yield return ListRow().Box(row);
        yield return ListRow().PseudoNormal().Prop(Control.StylePropertyModulateSelf, Color.FromHex("#1C1A15"));
        yield return ListRow().PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.FromHex("#363022"));
        yield return ListRow().PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.FromHex("#6A5626"));
        yield return ListRow().PseudoDisabled().Prop(Control.StylePropertyModulateSelf, Color.FromHex("#121110"));
        yield return E<ItemList>()
            .Prop(ItemList.StylePropertyBackground, ST14HubStyle.Flat(Color.FromHex("#141310")))
            .Prop(ItemList.StylePropertyItemBackground, ListBox(Color.FromHex("#1C1A15")))
            .Prop(ItemList.StylePropertyDisabledItemBackground, ListBox(Color.FromHex("#121110")))
            .Prop(ItemList.StylePropertySelectedItemBackground, ListBox(Color.FromHex("#6A5626")));

        // drop downs: black field with a gold chevron
        var drop = ST14HubStyle.Flat(Color.Black, ST14HubStyle.Gold.WithAlpha(0.4f), 1);
        drop.SetContentMarginOverride(StyleBox.Margin.Left, 8);
        drop.SetContentMarginOverride(StyleBox.Margin.Right, 6);
        drop.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        yield return E<OptionButton>().Class(ContainerButton.StyleClassButton).Box(drop);
        yield return E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle)
            .Prop(TextureRect.StylePropertyTexture, ST14HubStyle.Tex(cache, "dropdownarrow.png"))
            .Modulate(ST14HubStyle.Gold)
            .Margin(new Thickness(6, 0, 0, 0));
        yield return E<Label>().Class(OptionButton.StyleClassOptionButton).FontColor(ST14HubStyle.Gold);

        // text fields
        var field = ST14HubStyle.Flat(Field, ST14HubStyle.Gold.WithAlpha(0.35f), 1);
        field.SetContentMarginOverride(StyleBox.Margin.Horizontal, 6);
        field.SetContentMarginOverride(StyleBox.Margin.Vertical, 3);
        yield return E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, field);
        yield return E<LineEdit>().Prop(LineEdit.StylePropertySelectionColor, ST14HubStyle.Gold.WithAlpha(0.35f));
        yield return E<LineEdit>().Prop(LineEdit.StylePropertyCursorColor, ST14HubStyle.GoldLight);

        // check boxes of the original
        yield return E<TextureRect>().Class(CheckBox.StyleClassCheckBox)
            .Prop(TextureRect.StylePropertyTexture, ST14HubStyle.Tex(cache, "checkbox_off.png"))
            .Prop(TextureRect.StylePropertyTextureSizeTarget, new Vector2(23, 23));
        yield return E<TextureRect>().Class(CheckBox.StyleClassCheckBox).Class(CheckBox.StyleClassCheckBoxChecked)
            .Prop(TextureRect.StylePropertyTexture, ST14HubStyle.Tex(cache, "checkbox_on.png"))
            .Prop(TextureRect.StylePropertyTextureSizeTarget, new Vector2(23, 23));

        // plain sliders: dark textured groove, gold fill, thin light handle. Colour sliders keep their own look
        var groove = ST14HubStyle.Patch(cache, "editor_button.png", 8, 9, 9, 8, new Color(0.6f, 0.6f, 0.6f));
        groove.SetContentMarginOverride(StyleBox.Margin.All, 0);
        var fill = ST14HubStyle.Patch(cache, "slider_fill.png", 8, 8, 8, 8);
        fill.SetContentMarginOverride(StyleBox.Margin.All, 0);
        var handle = ST14HubStyle.Flat(Color.FromHex("#F5F5F5C8"));
        handle.SetContentMarginOverride(StyleBox.Margin.Horizontal, 1.5f);
        // the boxes above have no size of their own, the handle gives the slider its height or it collapses to nothing
        handle.SetContentMarginOverride(StyleBox.Margin.Vertical, 11);
        yield return E<Slider>()
            .Prop(Slider.StylePropertyBackground, groove)
            .Prop(Slider.StylePropertyForeground, new StyleBoxEmpty())
            .Prop(Slider.StylePropertyFill, fill)
            .Prop(Slider.StylePropertyGrabber, handle);

        // thin gold scroll bars
        yield return E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.Gold.WithAlpha(0.5f), true));
        yield return E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.Gold.WithAlpha(0.8f), true));
        yield return E<VScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.GoldLight, true));
        yield return E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.Gold.WithAlpha(0.5f), false));
        yield return E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.Gold.WithAlpha(0.8f), false));
        yield return E<HScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, Grabber(ST14HubStyle.GoldLight, false));

        // tabs as plates: the open one lit gold, the others dark, the page is dark glass
        var tab = Plate(cache);
        tab.Modulate = ST14HubStyle.GoldLight;
        tab.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        tab.SetContentMarginOverride(StyleBox.Margin.Vertical, 5);
        var tabOff = Plate(cache);
        tabOff.Modulate = new Color(0.7f, 0.7f, 0.7f);
        tabOff.SetContentMarginOverride(StyleBox.Margin.Horizontal, 14);
        tabOff.SetContentMarginOverride(StyleBox.Margin.Vertical, 5);
        var page = Glass(0.75f);
        page.SetContentMarginOverride(StyleBox.Margin.All, 6);
        yield return E<TabContainer>()
            .Prop(TabContainer.StylePropertyPanelStyleBox, page)
            .Prop(TabContainer.StylePropertyTabStyleBox, tab)
            .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabOff)
            .Prop(TabContainer.stylePropertyTabFontColor, ST14HubStyle.GoldLight)
            .Prop(TabContainer.StylePropertyTabFontColorInactive, ST14HubStyle.Gold.WithAlpha(0.75f));

        // panels, gold lines and headings
        yield return E<PanelContainer>().Class(StyleClass.BackgroundPanel).Panel(Glass());
        yield return E<PanelContainer>().Class(StyleClass.Highlight).Panel(ST14HubStyle.Flat(ST14HubStyle.Gold.WithAlpha(0.7f)));
        yield return E<Separator>().Class(StyleClass.Highlight).Prop(Separator.StylePropertyColor, ST14HubStyle.Gold.WithAlpha(0.7f));
        yield return E<Label>().Class(StyleClass.LabelHeading).Font(headingFont).FontColor(ST14HubStyle.GoldLight);
        yield return E<Label>().Class(StyleClass.LabelHeadingBigger).Font(headingBigFont).FontColor(ST14HubStyle.GoldLight);
    }

    private static MutableSelectorElement ListRow()
    {
        return E<ContainerButton>().Class(ContainerButton.StyleClassButton).Class(ListContainer.StyleClassListContainerButton);
    }

    private static StyleBoxFlat ListBox(Color color)
    {
        var box = ST14HubStyle.Flat(color);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 4);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        return box;
    }

    private static MutableSelectorElement Button()
    {
        return E<ContainerButton>().Class(ContainerButton.StyleClassButton);
    }

    private static StyleBoxFlat Grabber(Color color, bool vertical)
    {
        var box = ST14HubStyle.Flat(color);
        if (vertical)
        {
            box.ContentMarginLeftOverride = 6;
            box.ContentMarginTopOverride = 10;
        }
        else
        {
            box.ContentMarginTopOverride = 6;
            box.ContentMarginLeftOverride = 10;
        }

        return box;
    }
}
