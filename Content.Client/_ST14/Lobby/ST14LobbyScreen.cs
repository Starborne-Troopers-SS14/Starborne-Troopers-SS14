using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.MainHub;
using Content.Client.GameTicking;
using Content.Client.Info;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Voting.UI;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Lobby;

/// <summary>
/// Our own lobby screen. It covers the whole stock lobby, which stays hidden underneath, and only borrows the working
/// controls from it: the ready and observe buttons, the round timer, votes, chat, server info, the character preview
/// and later the parts of the character editor. Every panel gets an exact rectangle from <see cref="ArrangeOverride"/>,
/// computed from the window size, so panels can never run into each other
/// </summary>
public sealed partial class ST14LobbyScreen : Control
{
    public const string ScreenName = "ST14LobbyScreen";

    private const float TopHeight = 60;
    private const float BottomHeight = 40;
    private const float Gap = 14;

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IClientPreferencesManager _prefs = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private readonly IResourceCache _cache;
    private readonly LobbyGui _lobby;

    private readonly ST14AnimatedScene _lobbyScene;
    private readonly ST14AnimatedScene _editorScene;
    private readonly ST14SlideIn _left = new();
    private readonly ST14SlideIn _right = new();
    private readonly ST14SlideIn _editorSlide = new() { Visible = false };
    private readonly ST14CharacterScreen _editor;
    private readonly ST14SettingsScreen _settings = new() { Visible = false };
    private readonly ST14SupportScreen _support = new() { Visible = false };
    private Control _top = new();
    private Control _bottom = new();

    private readonly Label _status;
    private readonly Label _name;
    private readonly Label _job;

    private bool _editorShown;
    private bool _infoShown;
    private ST14LobbyThemePrototype? _theme;
    private ST14LobbyFrame? _briefing;
    private ST14LobbyFrame? _dossier;
    private ST14LobbyFrame? _links;
    private ST14LobbyFrame? _comms;
    private Button? _chatTab;
    private Button? _infoTab;
    private ST14Pedestal? _pedestal;
    private float _infoTimer;

    private ST14LobbyScreen(LobbyGui lobby, IResourceCache cache)
    {
        IoCManager.InjectDependencies(this);
        _lobby = lobby;
        _cache = cache;
        Name = ScreenName;

        _lobbyScene = new ST14AnimatedScene { ScanlineAlpha = 0.25f };
        _lobbyScene.SetScene("ST14LobbyOrbit");
        AddChild(_lobbyScene);

        _editorScene = new ST14AnimatedScene { ScanlineAlpha = 0.25f, Visible = false };
        _editorScene.SetScene("ST14LobbyHeadquarters");
        AddChild(_editorScene);

        _status = ST14HubStyle.Label(cache, string.Empty, ST14HubFont.Medium, 15, ST14LobbyStyle.Text);
        _name = ST14HubStyle.Label(cache, string.Empty, ST14HubFont.SemiBold, 18, ST14HubStyle.GoldLight);
        _job = ST14HubStyle.Label(cache, string.Empty, ST14HubFont.Regular, 14, ST14LobbyStyle.Text);

        _editor = new ST14CharacterScreen(cache);
    }

    /// <summary>
    /// Moves the stock controls in. Runs only after this screen is inside the lobby: UIScreen registers widgets
    /// like the chat box when a subtree is added to it, so a subtree that already holds the chat would be refused
    /// </summary>
    private void Build()
    {
        _top = BuildTopBar();
        AddChild(_top);
        _bottom = BuildBottomBar();
        AddChild(_bottom);

        _left.AddChild(BuildBriefing());
        AddChild(_left);

        _right.AddChild(BuildRightColumn());
        AddChild(_right);

        _editorSlide.AddChild(_editor);
        AddChild(_editorSlide);

        // settings open full screen like in the main menu, over everything else
        _settings.OnOpened += () =>
        {
            _lobbyScene.Visible = false;
            _editorScene.Visible = false;
        };
        _settings.OnClosed += () =>
        {
            _lobbyScene.Visible = !_editorShown;
            _editorScene.Visible = _editorShown;
        };
        AddChild(_settings);

        // the support screen of the main menu, also reachable from the lobby
        _support.OnBack += () => _support.Visible = false;
        AddChild(_support);

        // no side in the war yet: the choice over everything, it cannot be skipped
        if (_sideChoice != null)
        {
            AddChild(_sideChoice);
        }

        _left.Play(new Vector2(-90, 0), 0.5f, 0.05f);
        _right.Play(new Vector2(90, 0), 0.5f, 0.12f);

        // the moved preview lost its dummy entity on the way, load it again
        UserInterfaceManager.DeferAction(() => UserInterfaceManager.GetUIController<LobbyUIController>().ReloadCharacterSetup());
    }

    /// <summary>
    /// Puts the screen over the stock lobby once and hides everything else of it
    /// </summary>
    public static void Apply(LobbyGui lobby, IResourceCache cache, Stylesheet mainSheet)
    {
        if (lobby.Children.Any(c => c.Name == ScreenName))
            return;

        // the look comes from the main stylesheet, see ST14Stylesheet

        var screen = new ST14LobbyScreen(lobby, cache);
        foreach (var child in lobby.Children.ToList())
        {
            child.Visible = false;
        }

        lobby.AddChild(screen);
        LayoutContainer.SetAnchorAndMarginPreset(screen, LayoutContainer.LayoutPreset.Wide);
        screen.Build();
    }

    private static void Move(Control control, Control parent)
    {
        control.Orphan();
        parent.AddChild(control);
    }

    #region Building

    private Control BuildTopBar()
    {
        var bar = new PanelContainer { PanelOverride = TopBox() };
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(14, 0, 14, 2),
        };
        bar.AddChild(row);

        row.AddChild(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_cache, "news_planet.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            SetSize = new Vector2(40, 40),
            VerticalAlignment = VAlignment.Center,
        });

        var name = _lobby.ServerName;
        Move(name, row);
        name.HorizontalExpand = true;
        name.HorizontalAlignment = HAlignment.Stretch;
        name.VerticalAlignment = VAlignment.Center;
        name.FontOverride = ST14HubStyle.Font(_cache, ST14HubFont.SemiBold, 20);
        name.FontColorOverride = ST14HubStyle.GoldLight;
        name.ClipText = true;

        // AHelp, vote, settings, back to the main menu, leave
        var buttons = new List<Control> { _lobby.AHelpButton };
        if (_lobby.FindControl<VoteCallMenuButton>("CallVoteButton") is { } vote)
            buttons.Add(vote);
        // our full screen settings instead of the stock options window, the stock button stays hidden in the old lobby
        var settings = new Button { Text = _lobby.OptionsButton.Text, StyleClasses = { "ButtonBig" } };
        settings.OnPressed += _ => _settings.Show();
        buttons.Add(settings);

        var support = new Button
        {
            Text = Loc.GetString("st14-hub-button-support"),
            StyleClasses = { "ButtonBig" },
            ModulateSelfOverride = ST14HubStyle.Orange,
        };
        support.OnPressed += _ => _support.Visible = true;
        buttons.Insert(0, support);
        buttons.AddRange(_lobby.HeaderContainer.Children.Where(c => c is BaseButton).ToList());
        buttons.Add(_lobby.LeaveButton);

        foreach (var button in buttons)
        {
            Move(button, row);
            button.VerticalAlignment = VAlignment.Center;
            button.MinHeight = 40;
            button.Margin = new Thickness(0);
        }

        return bar;
    }

    private Control BuildBottomBar()
    {
        var bar = new PanelContainer { PanelOverride = ST14HubStyle.MainBar(_cache) };
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 30,
            Margin = new Thickness(16, 8, 16, 0),
        };
        bar.AddChild(row);

        Move(_lobby.LobbySong, row);
        Move(_lobby.LobbyBackground, row);
        foreach (var label in new[] { _lobby.LobbySong, _lobby.LobbyBackground })
        {
            label.VerticalAlignment = VAlignment.Center;
            label.HorizontalAlignment = HAlignment.Left;
        }

        return bar;
    }

    private Control BuildBriefing()
    {
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
        };

        var briefing = _briefing = new ST14LobbyFrame(_cache, Loc.GetString("st14-lobby-briefing"));
        column.AddChild(briefing);

        _status.Align = Label.AlignMode.Center;
        briefing.Body.AddChild(_status);

        var timer = _lobby.StartTime;
        Move(timer, briefing.Body);
        timer.Align = Label.AlignMode.Center;
        timer.HorizontalAlignment = HAlignment.Stretch;
        timer.FontOverride = ST14HubStyle.Font(_cache, ST14HubFont.Medium, 18);
        timer.FontColorOverride = ST14HubStyle.GoldLight;

        var station = _lobby.StationTime;
        Move(station, briefing.Body);
        station.HorizontalAlignment = HAlignment.Center;
        station.FontColorOverride = ST14LobbyStyle.Text;
        station.Margin = new Thickness(0);

        var ready = _lobby.ReadyButton;
        Move(ready, briefing.Body);
        ready.MinHeight = 62;
        ready.MinWidth = 0;
        ready.HorizontalExpand = true;
        ready.Margin = new Thickness(0, 6, 0, 0);
        ready.Label.FontOverride = ST14HubStyle.Font(_cache, ST14HubFont.SemiBold, 20);

        var observe = _lobby.ObserveButton;
        Move(observe, briefing.Body);
        observe.MinHeight = 44;
        observe.HorizontalExpand = true;

        var playtime = _lobby.PlaytimeComment;
        Move(playtime, briefing.Body);

        column.AddChild(BuildSide());

        // votes pile up under the briefing and scroll when there are many
        var votesScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        column.AddChild(votesScroll);
        var votes = _lobby.VoteContainer;
        Move(votes, votesScroll);
        votes.HorizontalAlignment = HAlignment.Stretch;
        votes.Margin = new Thickness(0);

        if (_lobby.FindControl<LinkBanner>("LinkBanner") is { } links)
        {
            var linksFrame = _links = new ST14LobbyFrame(_cache, Loc.GetString("st14-lobby-links"));
            column.AddChild(linksFrame);
            Move(links, linksFrame.Body);
            links.HorizontalAlignment = HAlignment.Center;
            links.Margin = new Thickness(0);
        }

        if (_lobby.FindControl<DevInfoBanner>("DevInfoBanner") is { } dev)
            Move(dev, column);

        return column;
    }

    private Control BuildRightColumn()
    {
        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 10,
        };

        // personnel file of the selected character
        var dossier = _dossier = new ST14LobbyFrame(_cache, Loc.GetString("st14-lobby-dossier"));
        column.AddChild(dossier);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
        };
        dossier.Body.AddChild(row);

        var preview = _lobby.CharacterPreview;
        var sprite = preview.ProfilePreviewSpriteView;
        sprite.Orphan();
        var pedestal = _pedestal = new ST14Pedestal(_cache) { SetSize = new Vector2(150, 160) };
        pedestal.AddChild(sprite);
        sprite.HorizontalAlignment = HAlignment.Center;
        sprite.VerticalAlignment = VAlignment.Bottom;
        sprite.Margin = new Thickness(0, 0, 0, 14);
        row.AddChild(pedestal);

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 6,
        };
        row.AddChild(text);

        _name.ClipText = true;
        text.AddChild(_name);
        _job.ClipText = true;
        text.AddChild(_job);

        var setup = preview.CharacterSetupButton;
        Move(setup, text);
        setup.StyleClasses.Add("ButtonBig");
        setup.HorizontalAlignment = HAlignment.Stretch;
        setup.MinHeight = 44;
        setup.Margin = new Thickness(0, 6, 0, 0);

        // radio and server info share one frame, switched by two plates
        var comms = _comms = new ST14LobbyFrame(_cache, Loc.GetString("st14-lobby-radio")) { VerticalExpand = true };
        column.AddChild(comms);

        var tabs = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        comms.Body.AddChild(tabs);

        var chatTab = new Button { Text = Loc.GetString("st14-lobby-radio"), ToggleMode = true, Pressed = true, HorizontalExpand = true };
        var infoTab = new Button { Text = Loc.GetString("st14-lobby-server-info"), ToggleMode = true, HorizontalExpand = true };
        var group = new ButtonGroup();
        chatTab.Group = group;
        infoTab.Group = group;
        tabs.AddChild(chatTab);
        tabs.AddChild(infoTab);
        _chatTab = chatTab;
        _infoTab = infoTab;

        var chat = _lobby.Chat;
        Move(chat, comms.Body);
        chat.VerticalExpand = true;
        chat.Margin = new Thickness(0);

        var infoScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Visible = false };
        comms.Body.AddChild(infoScroll);
        var info = _lobby.ServerInfo;
        Move(info, infoScroll);
        info.MaxWidth = float.PositiveInfinity;
        info.HorizontalAlignment = HAlignment.Stretch;
        info.VerticalAlignment = VAlignment.Top;
        info.VerticalExpand = false;

        // the selected plate is lit gold, the other one stays dark
        void Select(Button selected)
        {
            // the plates carry their own colour, the pressed tint of buttons would muddy the lit one
            chatTab.ModulateSelfOverride = Color.White;
            infoTab.ModulateSelfOverride = Color.White;
            chatTab.StyleBoxOverride = ST14LobbyStyle.Tab(_cache, selected == chatTab);
            infoTab.StyleBoxOverride = ST14LobbyStyle.Tab(_cache, selected == infoTab);
            chatTab.Label.FontColorOverride = selected == chatTab ? Color.Black : ST14HubStyle.Gold;
            infoTab.Label.FontColorOverride = selected == infoTab ? Color.Black : ST14HubStyle.Gold;
        }

        Select(chatTab);

        chatTab.OnPressed += _ =>
        {
            Select(chatTab);
            chat.Visible = true;
            infoScroll.Visible = false;
            _infoShown = false;
            comms.Title.Text = Text("radio");
        };
        infoTab.OnPressed += _ =>
        {
            Select(infoTab);
            chat.Visible = false;
            infoScroll.Visible = true;
            _infoShown = true;
            comms.Title.Text = Text("server-info");
        };

        return column;
    }

    private static Robust.Client.Graphics.StyleBoxFlat TopBox()
    {
        var box = ST14HubStyle.Flat(Color.FromHex("#0B0A08E8"));
        box.BorderColor = ST14HubStyle.Gold.WithAlpha(0.6f);
        box.BorderThickness = new Thickness(0, 0, 0, 1.5f);
        return box;
    }

    #endregion

    #region Layout

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
        var bodyTop = TopHeight + Gap;
        var bodyHeight = MathF.Max(0, h - TopHeight - BottomHeight - Gap * 2);

        // side columns take a share of the width, keep at least a bit of the background visible between them
        var leftWidth = Math.Clamp(w * 0.23f, 300, 430);
        var rightWidth = Math.Clamp(w * 0.28f, 340, 520);
        var spare = w - leftWidth - rightWidth - Gap * 3;
        if (spare < 0)
        {
            var k = (w - Gap * 3) / (leftWidth + rightWidth);
            leftWidth *= k;
            rightWidth *= k;
        }

        _lobbyScene.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        _editorScene.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        _top.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(w, TopHeight)));
        _bottom.Arrange(UIBox2.FromDimensions(new Vector2(0, h - BottomHeight), new Vector2(w, BottomHeight)));
        _left.Arrange(UIBox2.FromDimensions(new Vector2(Gap, bodyTop), new Vector2(leftWidth, bodyHeight)));
        _right.Arrange(UIBox2.FromDimensions(new Vector2(w - Gap - rightWidth, bodyTop), new Vector2(rightWidth, bodyHeight)));
        _editorSlide.Arrange(UIBox2.FromDimensions(new Vector2(Gap, bodyTop), new Vector2(w - Gap * 2, bodyHeight)));
        _settings.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        _support.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        _sideChoice?.Arrange(UIBox2.FromDimensions(Vector2.Zero, finalSize));
        return finalSize;
    }

    #endregion

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // LobbyGui.SwitchState turns the stock editor holder on and off, the pages follow it
        var editor = _lobby.CharacterSetupState.Visible;
        if (editor != _editorShown)
        {
            _editorShown = editor;
            _left.Visible = !editor;
            _right.Visible = !editor;
            _lobbyScene.Visible = !editor;
            _editorScene.Visible = editor;
            _editorSlide.Visible = editor;

            if (editor)
            {
                _editorSlide.Play(new Vector2(0, 50), 0.45f);
            }
            else
            {
                _left.Play(new Vector2(-90, 0), 0.45f);
                _right.Play(new Vector2(90, 0), 0.45f, 0.06f);
            }
        }

        if (editor && _lobby.CharacterSetupState.Children.FirstOrDefault(c => c is CharacterSetupGui) is CharacterSetupGui gui)
            _editor.Take(gui);

        _infoTimer -= args.DeltaSeconds;
        if (_infoTimer > 0)
            return;

        _infoTimer = 0.5f;
        UpdateInfo();
    }

    /// <summary>
    /// Text of the current theme, panel titles and status lines are in upper case
    /// </summary>
    private string Text(string key)
    {
        return Loc.GetString($"{_theme?.LocPrefix ?? "st14-lobby"}-{key}").ToUpperInvariant();
    }

    /// <summary>
    /// Trooper or bug lobby: the forced theme from the cvar, else the theme that lists the high priority job
    /// of the selected character, else the default one without jobs
    /// </summary>
    private ST14LobbyThemePrototype? PickTheme(HumanoidCharacterProfile? profile)
    {
        var forced = _cfg.GetCVar(ST14CVars.LobbyTheme);
        if (!string.IsNullOrEmpty(forced) && _protoMan.TryIndex<ST14LobbyThemePrototype>(forced, out var forcedTheme))
            return forcedTheme;

        var themes = _protoMan.EnumeratePrototypes<ST14LobbyThemePrototype>().ToList();

        // a player bound to a side sees its lobby whatever their job priorities say
        if (_entMan.System<ST14SideClientSystem>().Side is { } side)
        {
            return side == ST14GalaxyFaction.Arachnid && _protoMan.TryIndex(ST14GalaxyWarRules.BugTheme, out var bugs)
                ? bugs
                : themes.FirstOrDefault(t => t.Jobs.Count == 0);
        }

        var high = profile?.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
        if (high is { } job && job != default && themes.FirstOrDefault(t => t.Jobs.Contains(job)) is { } jobTheme)
            return jobTheme;

        return themes.FirstOrDefault(t => t.Jobs.Count == 0);
    }

    private void ApplyTheme(ST14LobbyThemePrototype theme)
    {
        var first = _theme == null;
        _theme = theme;

        foreach (var (frame, key) in new[] { (_briefing, "briefing"), (_dossier, "dossier"), (_links, "links"),
                     (_comms, _infoShown ? "server-info" : "radio") })
        {
            if (frame == null)
                continue;

            frame.Title.Text = Text(key);
            frame.Title.FontColorOverride = theme.Accent;
        }

        if (_chatTab != null)
            _chatTab.Text = Text("radio");
        if (_infoTab != null)
            _infoTab.Text = Text("server-info");

        if (_pedestal != null)
        {
            _pedestal.GlowColor = theme.Accent;
            _pedestal.RimColor = theme.StandLight;
        }

        _name.FontColorOverride = theme.Accent;
        _shownStarted = null;

        if (first)
        {
            _lobbyScene.SetScene(theme.Scene);
            return;
        }

        // the other side of the war: the background cuts over with the glitch and the panels come in again
        _lobbyScene.GlitchTo(theme.Scene);
        if (!_editorShown)
        {
            _left.Play(new Vector2(-90, 0), 0.45f, 0.15f);
            _right.Play(new Vector2(90, 0), 0.45f, 0.2f);
        }
    }

    private string? _shownName;
    private string? _shownJob;
    private bool? _shownStarted;

    /// <summary>
    /// Name and job of the selected character and the round state. Labels change only when the text does
    /// </summary>
    private void UpdateInfo()
    {
        var selected = _prefs.ServerDataLoaded ? _prefs.Preferences?.SelectedCharacter as HumanoidCharacterProfile : null;
        if (PickTheme(selected) is { } theme && theme != _theme)
            ApplyTheme(theme);

        var started = _entMan.System<ClientGameTicker>().IsGameStarted;
        if (started != _shownStarted)
        {
            _shownStarted = started;
            _status.Text = Text(started ? "status-round" : "status-lobby");
        }

        var name = string.Empty;
        var job = string.Empty;
        if (_prefs.ServerDataLoaded && _prefs.Preferences?.SelectedCharacter is HumanoidCharacterProfile profile)
        {
            name = string.IsNullOrWhiteSpace(profile.Name) ? Loc.GetString("st14-lobby-unnamed") : profile.Name;

            var high = profile.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
            if (high != default && _protoMan.TryIndex(high, out var jobProto))
                job = jobProto.LocalizedName;
            else
                job = Loc.GetString("st14-lobby-no-job");
        }
        else if (!_prefs.ServerDataLoaded)
        {
            name = Loc.GetString("st14-lobby-loading");
        }

        if (name != _shownName)
        {
            _shownName = name;
            _name.Text = name.ToUpperInvariant();
        }

        if (job != _shownJob)
        {
            _shownJob = job;
            _job.Text = job;
        }
    }
}
