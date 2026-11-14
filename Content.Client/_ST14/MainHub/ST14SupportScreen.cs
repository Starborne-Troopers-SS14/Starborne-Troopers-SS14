using System.Linq;
using System.Numerics;
using Content.Client._ST14.Lobby;
using Content.Client._ST14.Supporter;
using Content.Client.Lobby;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Content.Shared._ST14.CCVar;
using Content.Shared.Preferences;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Supporter tier of the local player, 0 is none and 1 to 4 are the Boosty sponsorships. The server sends it
/// (see ST14SupporterClientSystem); for now only the st14_sponsor test command sets it there
/// </summary>
public static class ST14SupporterStatus
{
    public static int Tier { get; private set; }

    public static bool IsSupporter => Tier > 0;

    public static event Action? Changed;

    public static void Set(int tier)
    {
        Tier = Math.Clamp(tier, 0, 4);
        Changed?.Invoke();
    }

    /// <summary>
    /// Accent of every tier: bronze, silver, gold and the red of the elite tier. Index 0 is "no subscription"
    /// </summary>
    public static readonly Color[] TierColors =
    {
        Color.FromHex("#8A8478"), Color.FromHex("#D08C4E"), Color.FromHex("#C9D4E0"), Color.FromHex("#F2C14E"), Color.FromHex("#E8604C"),
    };

    public static readonly string[] Numerals = { "—", "I", "II", "III", "IV" };
}

/// <summary>
/// "Support" screen of the main menu and the lobby, built like the mode selection of the original: four tall service
/// cards on a starfield, one per Boosty sponsorship, with rank chevrons, a slanted banner, the price and the perks, a metal
/// plate under each card and the way to Boosty in the bottom bar. A card or its plate opens the perks of that tier:
/// the ones that already work (OOC and ghost colour, weapon nickname) save the choice and send it to the server
/// </summary>
public sealed partial class ST14SupportScreen : ST14WarScreenBase
{
    [Dependency] private IUriOpener _uriOpener = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;

    public const string BoostyUrl = "https://boosty.to/corvaxcolonialmarines";

    // price and the number of perk lines of every tier, the texts are in the locale
    private static readonly (string Price, int Perks)[] Tiers = { ("300", 3), ("600", 2), ("1 300", 3), ("4 200", 2) };

    // cards of the tier row, relative to the screen centre in canvas units
    private const float CardStep = 440f;
    private const float CardY = -45f;
    private static readonly Vector2 CardSize = new(410, 600);

    private static readonly Color[] Swatches =
    {
        Color.FromHex("#F7D68C"), Color.FromHex("#FF6B5A"), Color.FromHex("#FF9A3C"), Color.FromHex("#7CE07C"),
        Color.FromHex("#5CBFE6"), Color.FromHex("#8C7CFF"), Color.FromHex("#E67CE0"), Color.FromHex("#FFFFFF"),
    };

    private readonly ST14TierMedal _medal;
    private readonly Label _status;
    private readonly List<Control> _rowParts = new();
    private readonly List<ST14SponsorCard> _cards = new();
    private readonly List<ST14HubButton> _perkPlates = new();
    private readonly List<ST14HubButton> _subscribePlates = new();

    // perks view: one frame, the tabs of the open tier, every perk page built once
    private readonly ST14LobbyFrame _perks;
    private readonly BoxContainer _perkTabs;
    private readonly ST14SponsorInsignia _perkInsignia;
    private readonly Label _perkPrice;
    private readonly RichTextLabel _perkList;
    private readonly Label _perkState;
    private readonly Dictionary<string, Control> _pages = new();
    private readonly List<(Label Label, int Tier, string Perk)> _locks = new();
    private readonly List<(Button Swatch, Color Color)> _oocSwatches = new();
    private readonly List<(Button Swatch, Color Color)> _ghostSwatches = new();
    private int _openTier;

    private Label _oocPreview = default!;
    private EntityPrototypeView _ghost = default!;
    private ProfilePreviewSpriteView _figure = default!;
    private ST14WoodenStand _stand = default!;
    private EntityPrototypeView _weaponView = default!;
    private Label _weaponPreview = default!;
    private Label _mentionName = default!;
    private List<EntityPrototype> _weapons = new();
    private int _weapon;
    private string _nickname = string.Empty;

    // the perk pages of every tier, keys of _pages
    private static readonly string[][] TierPages =
    {
        new[] { "ooc" },
        new[] { "ghost", "naming" },
        new[] { "figurine", "cosmetics" },
        new[] { "mention", "cosmetics" },
    };

    public ST14SupportScreen() : base("ST14SupportBackground")
    {
        BuildHeader();

        // the status widget top right, where the hub keeps its clock
        _medal = new ST14TierMedal(ResCache);
        Canvas.AddAt(_medal, new Vector2(1, 0), new Vector2(-60, 45), new Vector2(110, 110), new Vector2(1, 0));

        var statusCaption = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-status-header").ToUpperInvariant(), ST14HubFont.Medium, 15,
            ST14HubStyle.Widget);
        statusCaption.Align = Label.AlignMode.Right;
        Canvas.AddAt(statusCaption, new Vector2(1, 0), new Vector2(-185, 70), new Vector2(420, 26), new Vector2(1, 0));

        _status = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Bold, 26, Color.White, shadow: true);
        _status.Align = Label.AlignMode.Right;
        Canvas.AddAt(_status, new Vector2(1, 0), new Vector2(-185, 96), new Vector2(520, 40), new Vector2(1, 0));

        // four service cards with their plates
        for (var tier = 1; tier <= 4; tier++)
        {
            var x = (tier - 2.5f) * CardStep;
            var (price, count) = Tiers[tier - 1];
            var card = new ST14SponsorCard(ResCache, tier, price, count);
            var openTier = tier;
            card.OnPressed += _ => OpenTier(openTier);
            Canvas.AddAt(card, new Vector2(0.5f, 0.5f), new Vector2(x, CardY), CardSize);
            _cards.Add(card);
            _rowParts.Add(card);

            // two plates under the card: what the tier gives, and the way to get it
            var plateY = CardY + CardSize.Y / 2 + 4;
            var plate = new ST14HubButton(ResCache, Loc.GetString("st14-support-open-perks"), 18, ST14SupporterStatus.TierColors[tier]);
            plate.OnPressed += _ => OpenTier(openTier);
            Canvas.AddAt(plate, new Vector2(0.5f, 0.5f), new Vector2(x - 103, plateY), new Vector2(200, 60));
            _rowParts.Add(plate);
            _perkPlates.Add(plate);

            var subscribe = new ST14HubButton(ResCache, Loc.GetString("st14-support-subscribe"), 18, ST14HubStyle.Orange, Color.White)
            {
                ToolTip = BoostyUrl,
            };
            subscribe.OnPressed += _ => _uriOpener.OpenUri(BoostyUrl);
            Canvas.AddAt(subscribe, new Vector2(0.5f, 0.5f), new Vector2(x + 103, plateY), new Vector2(200, 60));
            _rowParts.Add(subscribe);
            _subscribePlates.Add(subscribe);
        }

        // how it works: a dark strip with the three numbered steps and the promise that nothing of it helps in battle
        var how = new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(Color.FromHex("#0A0A08D8"), ST14HubStyle.Gold.WithAlpha(0.35f), 1),
        };
        var howColumn = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(20, 8),
        };
        how.AddChild(howColumn);
        var steps = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 40,
            HorizontalAlignment = HAlignment.Center,
        };
        var howTitle = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-how").ToUpperInvariant(), ST14HubFont.Bold, 16, ST14HubStyle.GoldLight,
            shadow: true);
        howTitle.VAlign = Label.VAlignMode.Center;
        steps.AddChild(howTitle);
        for (var i = 1; i <= 3; i++)
        {
            var step = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10 };
            var number = ST14HubStyle.Label(ResCache, $"{i}", ST14HubFont.Black, 26, ST14HubStyle.Orange, shadow: true);
            number.VAlign = Label.VAlignMode.Center;
            step.AddChild(number);
            var text = ST14HubStyle.Label(ResCache, Loc.GetString($"st14-support-step-{i}"), ST14HubFont.Regular, 15, ST14LobbyStyle.Text, shadow: true);
            text.VAlign = Label.VAlignMode.Center;
            step.AddChild(text);
            steps.AddChild(step);
        }

        howColumn.AddChild(steps);
        var fair = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-fair"), ST14HubFont.Italic, 14, ST14HubStyle.Widget);
        fair.Align = Label.AlignMode.Center;
        howColumn.AddChild(fair);
        Canvas.AddAt(how, new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(1500, 84));
        _rowParts.Add(how);

        // perks of one tier, opened from a card
        _perks = new ST14LobbyFrame(ResCache, string.Empty) { Visible = false };
        Canvas.AddAt(_perks, new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1640, 770));

        var perkRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 16, VerticalExpand = true };
        _perks.Body.AddChild(perkRow);

        // left: the insignia and what the tier gives
        var side = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, SetWidth = 330 };
        perkRow.AddChild(side);
        _perkInsignia = new ST14SponsorInsignia(ResCache) { SetHeight = 230, Banner = true };
        side.AddChild(_perkInsignia);
        _perkPrice = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Medium, 16, ST14LobbyStyle.Text);
        _perkPrice.Align = Label.AlignMode.Center;
        side.AddChild(_perkPrice);
        _perkState = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.SemiBold, 15, ST14HubStyle.Gold);
        _perkState.Align = Label.AlignMode.Center;
        side.AddChild(_perkState);
        _perkList = new RichTextLabel { MaxWidth = 320, Margin = new Thickness(6, 8, 6, 0) };
        side.AddChild(_perkList);

        var main = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
        perkRow.AddChild(main);
        _perkTabs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        main.AddChild(_perkTabs);

        BuildPages(main);

        AddBottomBar();

        // the way to Boosty sits in the bottom bar like the plates of the hub
        var boosty = new ST14HubButton(ResCache, Loc.GetString("st14-support-boosty"), 22, ST14HubStyle.Orange, Color.White)
        {
            ToolTip = BoostyUrl,
        };
        boosty.OnPressed += _ => _uriOpener.OpenUri(BoostyUrl);
        Canvas.AddAt(boosty, new Vector2(1, 1), new Vector2(-30, -54), new Vector2(440, 70), new Vector2(1, 0.5f));

        var note = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-boosty-link"), ST14HubFont.Regular, 14, ST14HubStyle.Widget);
        note.Align = Label.AlignMode.Right;
        note.VAlign = Label.VAlignMode.Center;
        Canvas.AddAt(note, new Vector2(1, 1), new Vector2(-490, -54), new Vector2(600, 30), new Vector2(1, 0.5f));

        SelectWeapon(_weapon);
        UpdateStatus();
        ST14SupporterStatus.Changed += UpdateStatus;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        ST14SupporterStatus.Changed -= UpdateStatus;
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);

        // every visit starts at the cards; the figurine is the character the player has selected right now
        if (!newVisible)
            return;

        ShowCards();
        LoadFigure();
    }

    /// <summary>
    /// The exit icon steps back from the perks to the cards first
    /// </summary>
    protected override void GoBack()
    {
        if (_openTier != 0)
        {
            ShowCards();
            return;
        }

        base.GoBack();
    }

    #region Building

    private void BuildHeader()
    {
        // dark strip with a gold line under it, the title on the left like the screen names of the original
        Canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(ResCache, "mask_bottom.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = Color.Black.WithAlpha(0.85f),
        }, Vector2.Zero, new Vector2(1, 0), Vector2.Zero, new Vector2(0, 200), new Vector2(0.5f, 0));

        Canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(ResCache, "objectivebanner.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = ST14HubStyle.NewsBar,
        }, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 160), new Vector2(0, 5), new Vector2(0.5f, 0));

        var title = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-title").ToUpperInvariant(), ST14HubFont.Black, 46,
            ST14HubStyle.GoldLight, shadow: true);
        Canvas.AddAt(title, Vector2.Zero, new Vector2(70, 40), new Vector2(1100, 66), Vector2.Zero);

        var subtitle = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-subtitle"), ST14HubFont.Slogan, 22, ST14HubStyle.News,
            shadow: true);
        Canvas.AddAt(subtitle, Vector2.Zero, new Vector2(74, 106), new Vector2(1100, 36), Vector2.Zero);
    }

    private void BuildPages(BoxContainer main)
    {
        // OOC name colour (tier 1)
        var ooc = Page("ooc", main);
        ooc.AddChild(Lock(1, "st14-support-perk-ooc"));
        var oocCard = Card();
        ooc.AddChild(oocCard.Root);
        var chat = new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(Color.FromHex("#0A0A08F2"), ST14HubStyle.Gold.WithAlpha(0.35f), 1),
            VerticalExpand = true,
        };
        var chatLines = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(14, 12) };
        chat.AddChild(chatLines);
        chatLines.AddChild(ChatLine(Loc.GetString("st14-support-ooc-other-1"), Color.FromHex("#C9C2B0")));
        _oocPreview = ChatLine(Loc.GetString("st14-support-ooc-preview", ("name", _player.LocalSession?.Name ?? "?")), Swatches[0]);
        chatLines.AddChild(_oocPreview);
        chatLines.AddChild(ChatLine(Loc.GetString("st14-support-ooc-other-2"), Color.FromHex("#C9C2B0")));
        oocCard.Body.AddChild(chat);
        oocCard.Body.AddChild(SectionTitle("st14-support-pick-colour"));
        oocCard.Body.AddChild(SwatchGrid(_oocSwatches, color =>
        {
            _oocPreview.FontColorOverride = color;
            SaveChoice(ST14CVars.PerkOocColor, color.ToHex());
        }));
        ooc.AddChild(Text("st14-support-colours-text", 1150));

        // ghost colour (tier 2)
        var ghostPage = Page("ghost", main);
        ghostPage.AddChild(Lock(2, "st14-support-perk-ghost"));
        var ghostCard = Card();
        ghostPage.AddChild(ghostCard.Root);
        var ghostStand = new ST14Pedestal(ResCache) { SetSize = new Vector2(260, 300), HorizontalAlignment = HAlignment.Center, VerticalExpand = true };
        _ghost = new EntityPrototypeView
        {
            Scale = new Vector2(6, 6),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 24),
        };
        _ghost.SetPrototype("MobObserver");
        ghostStand.AddChild(_ghost);
        ghostCard.Body.AddChild(ghostStand);
        ghostCard.Body.AddChild(SectionTitle("st14-support-pick-colour"));
        ghostCard.Body.AddChild(SwatchGrid(_ghostSwatches, color =>
        {
            _ghost.Modulate = color;
            SaveChoice(ST14CVars.PerkGhostColor, color.ToHex());
        }));
        ghostPage.AddChild(Text("st14-support-colours-text", 1150));

        // what was picked before
        if (Color.TryFromHex(_cfg.GetCVar(ST14CVars.PerkOocColor), out var oocColor))
        {
            _oocPreview.FontColorOverride = oocColor;
            MarkSwatch(_oocSwatches, oocColor);
        }
        else
        {
            MarkSwatch(_oocSwatches, Swatches[0]);
        }

        if (Color.TryFromHex(_cfg.GetCVar(ST14CVars.PerkGhostColor), out var ghostColor))
        {
            _ghost.Modulate = ghostColor;
            MarkSwatch(_ghostSwatches, ghostColor);
        }

        BuildNaming(main);
        BuildFigurine(main);

        // cosmetics are not made yet: a stamp says so
        var cosmetics = Page("cosmetics", main);
        cosmetics.AddChild(Lock(3, "st14-support-perk-cosmetics"));
        cosmetics.AddChild(new Control { VerticalExpand = true });
        cosmetics.AddChild(new ST14StampControl(ResCache, Loc.GetString("st14-support-soon"))
        {
            SetSize = new Vector2(520, 120),
            HorizontalAlignment = HAlignment.Center,
        });
        var soon = Text("st14-support-cosmetics-text", 900);
        soon.HorizontalAlignment = HAlignment.Center;
        cosmetics.AddChild(soon);
        cosmetics.AddChild(new Control { VerticalExpand = true });

        // the round end mention (tier 4): how the closing report thanks the supporter
        var mention = Page("mention", main);
        mention.AddChild(Lock(4, "st14-support-perk-mention"));
        mention.AddChild(new Control { VerticalExpand = true });
        var report = new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(Color.FromHex("#0A0A08F2"), ST14SupporterStatus.TierColors[4].WithAlpha(0.6f), 2),
            HorizontalAlignment = HAlignment.Center,
            MinWidth = 760,
        };
        var reportLines = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, Margin = new Thickness(24, 18) };
        report.AddChild(reportLines);
        var reportTitle = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-mention-title").ToUpperInvariant(), ST14HubFont.Bold, 22,
            ST14HubStyle.GoldLight);
        reportTitle.Align = Label.AlignMode.Center;
        reportLines.AddChild(reportTitle);
        var reportThanks = ST14HubStyle.Label(ResCache, Loc.GetString("st14-support-mention-thanks"), ST14HubFont.Regular, 17, ST14LobbyStyle.Text);
        reportThanks.Align = Label.AlignMode.Center;
        reportLines.AddChild(reportThanks);
        _mentionName = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Black, 26, ST14SupporterStatus.TierColors[4], shadow: true);
        _mentionName.Align = Label.AlignMode.Center;
        reportLines.AddChild(_mentionName);
        mention.AddChild(report);
        var mentionText = Text("st14-support-mention-text", 900);
        mentionText.HorizontalAlignment = HAlignment.Center;
        mention.AddChild(mentionText);
        mention.AddChild(new Control { VerticalExpand = true });
    }

    private void BuildNaming(BoxContainer main)
    {
        // naming an item: the trooper guns, the engineer tool pistol is not a weapon to name
        var naming = Page("naming", main);
        naming.AddChild(Lock(2, "st14-support-perk-naming"));
        _weapons = _protoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && p.ID.StartsWith("ST14Weapon") && !p.ID.Contains("Engineer") && p.Components.ContainsKey("Gun"))
            .OrderBy(p => p.Name)
            .ToList();
        var weaponRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 14, VerticalExpand = true };

        var listCard = Card();
        listCard.Root.HorizontalExpand = false;
        listCard.Root.SetWidth = 300;
        listCard.Body.AddChild(SectionTitle("st14-support-weapons-header"));
        var list = new ItemList { VerticalExpand = true, SelectMode = ItemList.ItemListSelectMode.Single };
        foreach (var weapon in _weapons)
        {
            list.AddItem(weapon.Name);
        }

        list.OnItemSelected += args =>
        {
            SelectWeapon(args.ItemIndex);
            SaveChoice(ST14CVars.PerkWeapon, _weapons[_weapon].ID);
        };
        listCard.Body.AddChild(list);
        weaponRow.AddChild(listCard.Root);

        // display case: the gun on a lit stand and a brass plate with its new name
        var caseCard = Card();
        weaponRow.AddChild(caseCard.Root);
        var display = new ST14Pedestal(ResCache) { SetHeight = 260, HorizontalExpand = true };
        _weaponView = new EntityPrototypeView
        {
            Scale = new Vector2(8, 8),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 0, 30),
        };
        display.AddChild(_weaponView);
        caseCard.Body.AddChild(display);

        var plate = new PanelContainer
        {
            PanelOverride = ST14HubStyle.Flat(Color.FromHex("#C9A253"), Color.FromHex("#6B5226"), 2),
            HorizontalAlignment = HAlignment.Center,
            MinWidth = 420,
        };
        _weaponPreview = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.SemiBold, 20, Color.FromHex("#2A1A08"));
        _weaponPreview.Align = Label.AlignMode.Center;
        _weaponPreview.ClipText = true;
        _weaponPreview.Margin = new Thickness(16, 6);
        plate.AddChild(_weaponPreview);
        caseCard.Body.AddChild(plate);

        var inputRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8, HorizontalAlignment = HAlignment.Center };
        var input = new LineEdit { PlaceHolder = Loc.GetString("st14-support-nickname-placeholder"), MinHeight = 38, MinWidth = 360 };
        input.OnTextChanged += args =>
        {
            _nickname = args.Text;
            UpdateWeapon();
        };
        input.OnTextEntered += _ => SaveNickname();
        inputRow.AddChild(input);
        var save = new Button { Text = Loc.GetString("st14-support-nickname-save"), StyleClasses = { "ButtonBig" }, MinWidth = 220, MinHeight = 38 };
        save.OnPressed += _ => SaveNickname();
        inputRow.AddChild(save);
        caseCard.Body.AddChild(inputRow);
        caseCard.Body.AddChild(Text("st14-support-nickname-text", 760));
        naming.AddChild(weaponRow);

        // the weapon and nickname picked before
        var savedWeapon = _weapons.FindIndex(w => w.ID == _cfg.GetCVar(ST14CVars.PerkWeapon));
        _weapon = Math.Max(0, savedWeapon);
        _nickname = _cfg.GetCVar(ST14CVars.PerkNickname);
        input.Text = _nickname;
        if (_weapons.Count > 0)
            list[_weapon].Selected = true;
    }

    private void BuildFigurine(BoxContainer main)
    {
        // figurine of the selected character on a wooden stand
        var figurine = Page("figurine", main);
        figurine.AddChild(Lock(3, "st14-support-perk-figurine"));
        var figureRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 14, VerticalExpand = true };
        var standCard = Card();
        standCard.Root.HorizontalExpand = false;
        standCard.Root.SetWidth = 380;
        _stand = new ST14WoodenStand(ResCache) { VerticalExpand = true, HorizontalExpand = true, MinHeight = 400 };
        _figure = new ProfilePreviewSpriteView
        {
            Scale = new Vector2(5, 5),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 92),
            OverrideDirection = Direction.South,
        };
        _stand.AddChild(_figure);
        standCard.Body.AddChild(_stand);
        figureRow.AddChild(standCard.Root);

        var aboutCard = Card();
        aboutCard.Body.AddChild(new Control { VerticalExpand = true });
        aboutCard.Body.AddChild(SectionTitle("st14-support-figurine-title"));
        aboutCard.Body.AddChild(Text("st14-support-figurine-text", 700));
        aboutCard.Body.AddChild(Text("st14-support-figurine-note", 700));
        aboutCard.Body.AddChild(new Control { VerticalExpand = true });
        figureRow.AddChild(aboutCard.Root);
        figurine.AddChild(figureRow);
    }

    private BoxContainer Page(string key, BoxContainer parent)
    {
        var page = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            SeparationOverride = 10,
            Margin = new Thickness(4, 6, 4, 4),
            Visible = false,
        };
        parent.AddChild(page);
        _pages[key] = page;
        return page;
    }

    private Label SectionTitle(string loc)
    {
        var label = ST14HubStyle.Label(ResCache, Loc.GetString(loc).ToUpperInvariant(), ST14HubFont.SemiBold, 17, ST14HubStyle.GoldLight);
        label.Margin = new Thickness(6, 6, 6, 0);
        return label;
    }

    /// <summary>
    /// Line above a perk preview: the perk name and whether the current tier has it
    /// </summary>
    private Label Lock(int tier, string perk)
    {
        var label = ST14HubStyle.Label(ResCache, string.Empty, ST14HubFont.Medium, 16, ST14HubStyle.Gold);
        _locks.Add((label, tier, perk));
        return label;
    }

    /// <summary>
    /// Framed card of a page: dark glass with a thin gold edge, content in <see cref="CardParts.Body"/>
    /// </summary>
    private static CardParts Card()
    {
        var box = ST14LobbyStyle.Glass(0.65f);
        box.SetContentMarginOverride(StyleBox.Margin.All, 12);
        var root = new PanelContainer { PanelOverride = box, HorizontalExpand = true, VerticalExpand = true };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
        root.AddChild(body);
        return new CardParts(root, body);
    }

    private readonly record struct CardParts(PanelContainer Root, BoxContainer Body);

    private Label ChatLine(string text, Color color)
    {
        var line = ST14HubStyle.Label(ResCache, text, ST14HubFont.Regular, 17, color);
        line.ClipText = true;
        return line;
    }

    /// <summary>
    /// Two rows of colour chips, the picked one gets a thick light gold ring
    /// </summary>
    private static GridContainer SwatchGrid(List<(Button Swatch, Color Color)> swatches, Action<Color> onPick)
    {
        var grid = new GridContainer { Columns = 4, HSeparationOverride = 8, VSeparationOverride = 8, HorizontalAlignment = HAlignment.Center };
        foreach (var color in Swatches)
        {
            var swatch = new Button { MinSize = new Vector2(72, 42), ModulateSelfOverride = Color.White };
            swatch.OnPressed += _ =>
            {
                MarkSwatch(swatches, color);
                onPick(color);
            };
            grid.AddChild(swatch);
            swatches.Add((swatch, color));
        }

        MarkSwatch(swatches, Color.Transparent);
        return grid;
    }

    private static void MarkSwatch(List<(Button Swatch, Color Color)> swatches, Color picked)
    {
        foreach (var (swatch, color) in swatches)
        {
            var selected = color.ToHex() == picked.WithAlpha(1f).ToHex();
            var box = ST14HubStyle.Flat(color, selected ? ST14HubStyle.GoldLight : Color.Black.WithAlpha(0.6f), selected ? 3 : 1);
            swatch.StyleBoxOverride = box;
            swatch.Text = selected ? "✓" : string.Empty;
            swatch.Label.FontColorOverride = Color.Black;
        }
    }

    private static RichTextLabel Text(string loc, float maxWidth)
    {
        // rich text measures itself on one line inside the canvas, the width cap makes it wrap inside the frame
        var text = new RichTextLabel { Margin = new Thickness(6, 4, 6, 4), MaxWidth = maxWidth };
        if (loc.Length > 0)
            text.SetMessage(FormattedMessage.FromMarkupOrThrow(Loc.GetString(loc)));
        return text;
    }

    #endregion

    /// <summary>
    /// Back to the four cards
    /// </summary>
    private void ShowCards()
    {
        _openTier = 0;
        _perks.Visible = false;
        foreach (var part in _rowParts)
        {
            part.Visible = true;
        }
    }

    /// <summary>
    /// Opens the perks of a tier in place of the cards
    /// </summary>
    public void OpenTier(int tier)
    {
        _openTier = Math.Clamp(tier, 1, 4);
        foreach (var part in _rowParts)
        {
            part.Visible = false;
        }

        var accent = ST14SupporterStatus.TierColors[_openTier];
        var name = $"SPONSORSHIP {ST14SupporterStatus.Numerals[_openTier]}";
        _perks.Title.Text = Loc.GetString("st14-support-perks-of", ("tier", name)).ToUpperInvariant();
        _perks.Title.FontColorOverride = accent;
        _perkInsignia.Tier = _openTier;
        _perkPrice.Text = Loc.GetString("st14-support-tier-price", ("price", Tiers[_openTier - 1].Price));

        _perkTabs.RemoveAllChildren();
        var pages = TierPages[_openTier - 1];
        foreach (var key in pages)
        {
            var tab = new Button { Text = Loc.GetString($"st14-support-tab-{key}"), HorizontalExpand = true, MinHeight = 40 };
            var page = key;
            tab.OnPressed += _ => SelectPage(page);
            _perkTabs.AddChild(tab);
        }

        _perks.Visible = true;
        UpdatePerkSide();
        SelectPage(pages[0]);
    }

    private void SelectPage(string key)
    {
        foreach (var (pageKey, page) in _pages)
        {
            page.Visible = pageKey == key;
        }

        var name = Loc.GetString($"st14-support-tab-{key}");
        foreach (var tab in _perkTabs.Children.OfType<Button>())
        {
            var selected = tab.Text == name;
            tab.ModulateSelfOverride = Color.White;
            tab.StyleBoxOverride = ST14LobbyStyle.Tab(ResCache, selected);
            tab.Label.FontColorOverride = selected ? Color.Black : ST14HubStyle.Gold;
        }
    }

    private void SelectWeapon(int index)
    {
        if (_weapons.Count == 0)
            return;

        _weapon = Math.Clamp(index, 0, _weapons.Count - 1);
        _weaponView.SetPrototype(_weapons[_weapon].ID);
        UpdateWeapon();
    }

    /// <summary>
    /// Keeps a perk choice in the client cvars and sends all choices to the server, which uses them if the tier allows
    /// </summary>
    private void SaveChoice(CVarDef<string> cvar, string value)
    {
        _cfg.SetCVar(cvar, value);
        _cfg.SaveToFile();
        _entMan.System<ST14SupporterClientSystem>().SendChoices();
    }

    private void SaveNickname()
    {
        if (_weapons.Count > 0)
            _cfg.SetCVar(ST14CVars.PerkWeapon, _weapons[_weapon].ID);
        SaveChoice(ST14CVars.PerkNickname, _nickname.Trim());
    }

    private void UpdateWeapon()
    {
        var nick = string.IsNullOrWhiteSpace(_nickname) ? Loc.GetString("st14-support-nickname-default") : _nickname.Trim();
        if (nick.Length > 24)
            nick = nick[..24];

        var weapon = _weapons.Count > 0 ? _weapons[_weapon].Name : "?";
        _weaponPreview.Text = Loc.GetString("st14-support-nickname-preview", ("weapon", weapon), ("nickname", nick));
    }

    private void LoadFigure()
    {
        var name = _player.LocalSession?.Name ?? string.Empty;
        if (_prefs.ServerDataLoaded && _prefs.Preferences?.SelectedCharacter is HumanoidCharacterProfile profile)
        {
            _figure.LoadPreview(profile);
            if (!string.IsNullOrWhiteSpace(profile.Name))
                name = profile.Name;
        }

        _stand.Plate = name;
        _mentionName.Text = (_player.LocalSession?.Name ?? name).ToUpperInvariant();
    }

    private void UpdatePerkSide()
    {
        if (_openTier == 0)
            return;

        var tier = ST14SupporterStatus.Tier;
        var open = _openTier <= tier;
        _perkState.Text = Loc.GetString(_openTier == tier ? "st14-support-tier-current" : open
            ? "st14-support-tier-included"
            : "st14-support-tier-locked").ToUpperInvariant();
        _perkState.FontColorOverride = _openTier == tier ? ST14HubStyle.GoldLight : open ? Color.FromHex("#9CE07C") : Color.FromHex("#8A8478");
        _perkList.SetMessage(FormattedMessage.FromMarkupOrThrow(ST14SponsorCard.PerkMarkup(_openTier, Tiers[_openTier - 1].Perks, open)));
    }

    private void UpdateStatus()
    {
        var tier = ST14SupporterStatus.Tier;
        _medal.Tier = tier;
        _status.Text = (tier > 0 ? $"SPONSORSHIP {ST14SupporterStatus.Numerals[tier]}" : Loc.GetString("st14-support-status-none")).ToUpperInvariant();
        _status.FontColorOverride = tier > 0 ? ST14SupporterStatus.TierColors[tier] : Color.FromHex("#B8B0A0");

        foreach (var card in _cards)
        {
            card.Update(tier);
        }

        // an open tier is set up, not bought: its plate says so and the way to Boosty is only for the tiers above
        for (var i = 0; i < _perkPlates.Count; i++)
        {
            var open = i + 1 <= tier;
            _perkPlates[i].Text = Loc.GetString(open ? "st14-support-set-up" : "st14-support-open-perks");
            _subscribePlates[i].Disabled = open;
            _subscribePlates[i].Text = Loc.GetString(open ? "st14-support-subscribed" : "st14-support-subscribe");
        }

        foreach (var (label, lockTier, perk) in _locks)
        {
            var open = tier >= lockTier;
            label.Text = Loc.GetString(open ? "st14-support-perk-open" : "st14-support-perk-locked",
                ("perk", Loc.GetString(perk)), ("tier", ST14SupporterStatus.Numerals[lockTier])).ToUpperInvariant();
            label.FontColorOverride = open ? Color.FromHex("#9CE07C") : ST14SupporterStatus.TierColors[lockTier];
        }

        UpdatePerkSide();
    }
}

/// <summary>
/// Tall service card of one sponsorship, drawn like the mode cards of the original: a thin frame, the rank insignia on a
/// lit field, a slanted banner with the tier name, then the price and the perks. The player's own tier carries a stamp
/// </summary>
public sealed class ST14SponsorCard : ContainerButton
{
    private readonly IResourceCache _cache;
    private readonly StyleBoxTexture _frame;
    private readonly Texture _glow;
    private readonly RichTextLabel _perks;
    private readonly Label _badge;
    private readonly int _perkCount;

    public readonly int Tier;
    private int _current;

    public ST14SponsorCard(IResourceCache cache, int tier, string price, int perkCount)
    {
        _cache = cache;
        Tier = tier;
        _perkCount = perkCount;
        MuteSounds = true;
        StyleBoxOverride = new StyleBoxEmpty();
        DefaultCursorShape = CursorShape.Hand;

        _frame = new StyleBoxTexture
        {
            Texture = ST14HubStyle.WarTex(cache, "card_frame.png"),
            TextureScale = new Vector2(0.5f, 0.5f),
        };
        _frame.SetPatchMargin(StyleBox.Margin.All, 8);
        _glow = cache.GetResource<TextureResource>("/Textures/_ST14/Interface/Lobby/soft_glow.png").Texture;

        var accent = ST14SupporterStatus.TierColors[tier];
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(16, 10, 16, 40) };
        AddChild(column);

        _badge = ST14HubStyle.Label(cache, string.Empty, ST14HubFont.SemiBold, 13, ST14HubStyle.Gold, shadow: true);
        _badge.Align = Label.AlignMode.Right;
        column.AddChild(_badge);

        column.AddChild(new ST14SponsorInsignia(cache) { Tier = tier, Banner = true, SetHeight = 270, MouseFilter = MouseFilterMode.Ignore });

        var priceLabel = ST14HubStyle.Label(cache, $"{price} ₽", ST14HubFont.Black, 34, Color.White, shadow: true);
        priceLabel.Align = Label.AlignMode.Center;
        column.AddChild(priceLabel);
        var month = ST14HubStyle.Label(cache, Loc.GetString("st14-support-tier-month"), ST14HubFont.Regular, 14, ST14LobbyStyle.Text);
        month.Align = Label.AlignMode.Center;
        column.AddChild(month);

        var line = new PanelContainer { PanelOverride = ST14HubStyle.Flat(accent.WithAlpha(0.5f)), MinHeight = 1, Margin = new Thickness(20, 6, 20, 4) };
        column.AddChild(line);

        if (tier > 1)
        {
            var includes = ST14HubStyle.Label(cache, Loc.GetString("st14-support-tier-includes", ("tier", ST14SupporterStatus.Numerals[tier - 1])),
                ST14HubFont.Medium, 15, accent);
            includes.Align = Label.AlignMode.Center;
            column.AddChild(includes);
        }

        _perks = new RichTextLabel { MaxWidth = 345, Margin = new Thickness(2, 2, 2, 0), MouseFilter = MouseFilterMode.Ignore };
        column.AddChild(_perks);

        OnPressed += _ => Sounds.Play(ST14HubSound.Click);
        Update(0);
    }

    private ST14HubSoundController Sounds => UserInterfaceManager.GetUIController<ST14HubSoundController>();

    /// <summary>
    /// Perk lines of a tier: a green tick when the tier is open, a dim dot when not
    /// </summary>
    public static string PerkMarkup(int tier, int count, bool open)
    {
        var mark = open ? "[color=#9CE07C]✓[/color]" : "[color=#8A8478]▸[/color]";
        var textColor = open ? "#E6DCC0" : "#B8B0A0";
        var lines = Enumerable.Range(1, count)
            .Select(p => $"[font size=16]{mark} [color={textColor}]{Loc.GetString($"st14-support-tier-{tier}-perk-{p}")}[/color][/font]");
        return string.Join("\n", lines);
    }

    public void Update(int current)
    {
        _current = current;
        var open = Tier <= current;
        _badge.Text = Loc.GetString(Tier == current ? "st14-support-tier-current" : open
            ? "st14-support-tier-included"
            : Tier == 4 ? "st14-support-tier-max" : "st14-support-tier-locked").ToUpperInvariant();
        _badge.FontColorOverride = Tier == current ? ST14HubStyle.GoldLight : open ? Color.FromHex("#9CE07C") : Color.FromHex("#8A8478");
        _perks.SetMessage(FormattedMessage.FromMarkupOrThrow(PerkMarkup(Tier, _perkCount, open)));
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        Sounds.Play(ST14HubSound.Hover);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var size = PixelSize;
        var scale = size.Y / 600f;
        var hover = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed;
        var accent = ST14SupporterStatus.TierColors[Tier];
        var inner = new UIBox2(5 * scale, 5 * scale, size.X - 5 * scale, size.Y - 5 * scale);

        // dark field, lit from above in the tier colour, brighter under the mouse
        handle.DrawRect(inner, Color.FromHex("#0C0B09").WithAlpha(0.94f));
        handle.DrawTextureRect(_glow, new UIBox2(inner.Left - inner.Width * 0.3f, inner.Top - inner.Height * 0.25f,
            inner.Right + inner.Width * 0.3f, inner.Top + inner.Height * 0.7f), accent.WithAlpha(hover ? 0.32f : 0.2f));

        // thin scan lines like the screens of the original
        for (var y = inner.Top; y < inner.Bottom; y += 4 * scale)
        {
            handle.DrawRect(new UIBox2(inner.Left, y, inner.Right, y + MathF.Max(1, scale)), Color.Black.WithAlpha(0.18f));
        }

        var own = Tier == _current;
        _frame.Modulate = hover ? ST14HubStyle.GoldLight : own ? accent : Color.White;
        _frame.Draw(handle, UIBox2.FromDimensions(Vector2.Zero, size), scale);

        // the accent strip along the top edge
        handle.DrawRect(new UIBox2(inner.Left, inner.Top, inner.Right, inner.Top + 4 * scale), accent);

        base.Draw(handle);

        if (own)
        {
            ST14ClassifiedStamp.DrawStamp(handle, _cache, Loc.GetString("st14-support-tier-current"), new Vector2(size.X / 2, size.Y * 0.36f),
                new Vector2(300, 52) * scale, -8f, scale);
        }
    }
}

/// <summary>
/// Rank insignia of a sponsorship: one to three chevrons in the tier colour, a star over them for the top tier,
/// on a soft glow, and optionally the slanted banner with the tier name under it
/// </summary>
public sealed class ST14SponsorInsignia : Control
{
    private readonly IResourceCache _cache;
    private readonly Texture _glow;
    private float _time;

    public int Tier = 1;
    public bool Banner;

    public ST14SponsorInsignia(IResourceCache cache)
    {
        _cache = cache;
        _glow = cache.GetResource<TextureResource>("/Textures/_ST14/Interface/Lobby/soft_glow.png").Texture;
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
        var tier = Math.Clamp(Tier, 1, 4);
        var accent = ST14SupporterStatus.TierColors[tier];
        var unit = size.Y / 270f;
        var center = new Vector2(size.X / 2, (Banner ? 105 : 135) * unit);
        var pulse = 0.85f + 0.15f * MathF.Sin(_time * MathF.Tau / 3f);

        var glow = 230 * unit;
        handle.DrawTextureRect(_glow, UIBox2.FromDimensions(center - new Vector2(glow, glow) / 2, new Vector2(glow, glow)),
            accent.WithAlpha(0.3f * pulse));

        // chevrons stacked upwards, the top tier has three and a star
        var chevrons = Math.Min(tier, 3);
        var width = 150 * unit;
        var height = 52 * unit;
        var thick = 19 * unit;
        var step = 30 * unit;
        var top = center.Y - (chevrons - 1) * step / 2 + (tier == 4 ? 22 * unit : 0);
        for (var i = 0; i < chevrons; i++)
        {
            var c = new Vector2(center.X, top + i * step);
            Chevron(handle, c + new Vector2(0, 3 * unit), width, height, thick, Color.Black.WithAlpha(0.6f));
            Chevron(handle, c, width, height, thick, accent);
        }

        if (tier == 4)
        {
            var starCenter = new Vector2(center.X, top - 52 * unit);
            Star(handle, starCenter + new Vector2(0, 3 * unit), 30 * unit, Color.Black.WithAlpha(0.6f));
            Star(handle, starCenter, 30 * unit, accent);
        }

        if (!Banner)
            return;

        // slanted banner across the card like the banners baked into the mode cards
        var bannerCenter = new Vector2(size.X / 2, 228 * unit);
        var baseTransform = handle.GetTransform();
        handle.SetTransform(Matrix3x2.CreateRotation(-MathF.PI * 6.56f / 180f) * Matrix3x2.CreateTranslation(bannerCenter) * baseTransform);
        var half = new Vector2(MathF.Min(size.X * 0.54f, 220 * unit), 26 * unit);
        handle.DrawRect(new UIBox2(-half.X, -half.Y + 4 * unit, half.X, half.Y + 4 * unit), Color.Black.WithAlpha(0.45f));
        handle.DrawRect(new UIBox2(-half.X, -half.Y, half.X, half.Y), accent.WithAlpha(0.92f));
        handle.DrawRect(new UIBox2(-half.X, -half.Y, half.X, -half.Y + 2 * unit), Color.White.WithAlpha(0.35f));
        handle.SetTransform(baseTransform);

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Black, Math.Max(8, (int) MathF.Round(26 * unit)));
        ST14HubStyle.DrawRotatedText(handle, font, $"SPONSORSHIP {ST14SupporterStatus.Numerals[tier]}", bannerCenter, -6.56f,
            Color.FromHex("#14110C"));
    }

    private static void Chevron(DrawingHandleScreen handle, Vector2 c, float width, float height, float thick, Color color)
    {
        var apex = new Vector2(c.X, c.Y - height / 2);
        var apexIn = apex + new Vector2(0, thick);
        var left = new Vector2(c.X - width / 2, c.Y + height / 2);
        var leftIn = left + new Vector2(0, thick);
        var right = new Vector2(c.X + width / 2, c.Y + height / 2);
        var rightIn = right + new Vector2(0, thick);

        var verts = new Vector2[12];
        verts[0] = left;
        verts[1] = apex;
        verts[2] = apexIn;
        verts[3] = left;
        verts[4] = apexIn;
        verts[5] = leftIn;
        verts[6] = apex;
        verts[7] = right;
        verts[8] = rightIn;
        verts[9] = apex;
        verts[10] = rightIn;
        verts[11] = apexIn;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts, color);
    }

    private static void Star(DrawingHandleScreen handle, Vector2 c, float radius, Color color)
    {
        var verts = new Vector2[30];
        for (var i = 0; i < 10; i++)
        {
            var a0 = -MathF.PI / 2 + i * MathF.PI / 5;
            var a1 = -MathF.PI / 2 + (i + 1) * MathF.PI / 5;
            var r0 = i % 2 == 0 ? radius : radius * 0.42f;
            var r1 = i % 2 == 0 ? radius * 0.42f : radius;
            verts[i * 3] = c;
            verts[i * 3 + 1] = c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r0;
            verts[i * 3 + 2] = c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r1;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, verts, color);
    }
}

/// <summary>
/// Round medal of a supporter tier: a ring in the tier colour, a slowly pulsing glow and the tier numeral,
/// grey with a dash when there is no subscription
/// </summary>
public sealed class ST14TierMedal : Control
{
    private readonly IResourceCache _cache;
    private readonly Texture _glow;
    private float _time;

    public int Tier;

    public ST14TierMedal(IResourceCache cache)
    {
        _cache = cache;
        _glow = cache.GetResource<TextureResource>("/Textures/_ST14/Interface/Lobby/soft_glow.png").Texture;
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
        var center = size / 2;
        var radius = MathF.Min(size.X, size.Y) * 0.42f;
        var color = ST14SupporterStatus.TierColors[Math.Clamp(Tier, 0, 4)];
        var pulse = 0.8f + 0.2f * MathF.Sin(_time * MathF.Tau / 3f);

        if (Tier > 0)
        {
            var glow = radius * 2.4f;
            handle.DrawTextureRect(_glow, UIBox2.FromDimensions(center - new Vector2(glow, glow) / 2, new Vector2(glow, glow)),
                color.WithAlpha(0.35f * pulse));
        }

        handle.DrawCircle(center, radius, Color.FromHex("#14120E"));
        handle.DrawCircle(center, radius, color, false);
        handle.DrawCircle(center, radius * 0.86f, color.WithAlpha(0.6f), false);
        handle.DrawCircle(center, radius * 0.84f, color.WithAlpha(Tier > 0 ? 0.18f : 0.06f));

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Black, Math.Max(8, (int) (radius * 0.75f)));
        var text = ST14SupporterStatus.Numerals[Math.Clamp(Tier, 0, 4)];
        var dims = handle.GetDimensions(font, text, 1f);
        handle.DrawString(font, center - dims / 2, text, 1f, Tier > 0 ? color : color.WithAlpha(0.7f));
    }
}

/// <summary>
/// Slanted rubber stamp with a caption, like the classified stamps of the galaxy map
/// </summary>
public sealed class ST14StampControl : Control
{
    private readonly IResourceCache _cache;
    private readonly string _text;

    public ST14StampControl(IResourceCache cache, string text)
    {
        _cache = cache;
        _text = text;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var stamp = new Vector2(size.X * 0.9f, size.Y * 0.62f);
        ST14ClassifiedStamp.DrawStamp(handle, _cache, _text, size / 2, stamp, -6f, stamp.Y / 56f);
    }
}

/// <summary>
/// Wooden plinth for the character figurine: a soft spotlight from above, a shadow under the figure, a stepped wooden
/// base with bevels and grain and a brass plate carrying the character name
/// </summary>
public sealed class ST14WoodenStand : Control
{
    private readonly IResourceCache _cache;
    private readonly Texture _glow;

    public string Plate = string.Empty;

    public ST14WoodenStand(IResourceCache cache)
    {
        _cache = cache;
        _glow = cache.GetResource<TextureResource>("/Textures/_ST14/Interface/Lobby/soft_glow.png").Texture;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var size = PixelSize;
        var s = UIScale;
        var top = size.Y - 100 * s;
        var cx = size.X / 2;

        // spotlight falling on the figure and its shadow on the top board
        handle.DrawTextureRect(_glow, new UIBox2(cx - size.X * 0.45f, 0, cx + size.X * 0.45f, top + 10 * s),
            Color.FromHex("#F7D68C").WithAlpha(0.16f));
        handle.DrawTextureRect(_glow, new UIBox2(cx - 70 * s, top - 14 * s, cx + 70 * s, top + 10 * s), Color.Black.WithAlpha(0.55f));

        void Board(float left, float right, float y, float height, Color face)
        {
            var box = new UIBox2(cx - left * s, y, cx + right * s, y + height * s);
            handle.DrawRect(box, face);
            // bevel: light top edge, dark bottom edge
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + 2 * s), Color.White.WithAlpha(0.18f));
            handle.DrawRect(new UIBox2(box.Left, box.Bottom - 3 * s, box.Right, box.Bottom), Color.Black.WithAlpha(0.35f));
            // grain
            for (var i = 1; i < height / 7; i++)
            {
                var gy = y + i * 7 * s;
                handle.DrawLine(new Vector2(box.Left + 6 * s, gy), new Vector2(box.Right - 6 * s, gy + (i % 2 == 0 ? 1 : -1) * s),
                    Color.FromHex("#3A2210").WithAlpha(0.35f));
            }
        }

        Board(118, 118, top, 16, Color.FromHex("#8C5A32"));
        Board(100, 100, top + 16 * s, 56, Color.FromHex("#6E4526"));
        Board(132, 132, top + 72 * s, 18, Color.FromHex("#7C4E2B"));

        // brass name plate with screws
        var plate = new UIBox2(cx - 82 * s, top + 30 * s, cx + 82 * s, top + 58 * s);
        handle.DrawRect(plate, Color.FromHex("#C9A253"));
        handle.DrawRect(new UIBox2(plate.Left, plate.Top, plate.Right, plate.Top + 2 * s), Color.FromHex("#F2D58A"));
        handle.DrawRect(plate, Color.FromHex("#6B5226"), false);
        handle.DrawCircle(new Vector2(plate.Left + 6 * s, plate.Center.Y), 2 * s, Color.FromHex("#6B5226"));
        handle.DrawCircle(new Vector2(plate.Right - 6 * s, plate.Center.Y), 2 * s, Color.FromHex("#6B5226"));

        if (string.IsNullOrEmpty(Plate))
            return;

        var font = ST14HubStyle.Font(_cache, ST14HubFont.Medium, Math.Max(8, (int) (13 * s)));
        var text = Plate.Length > 22 ? Plate[..22] : Plate;
        var dims = handle.GetDimensions(font, text, 1f);
        handle.DrawString(font, plate.Center - dims / 2, text, 1f, Color.FromHex("#2A1A08"));
    }
}
