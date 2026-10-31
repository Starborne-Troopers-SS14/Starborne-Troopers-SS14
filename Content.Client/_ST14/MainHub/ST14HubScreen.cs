using System.Numerics;
using Content.Client.Changelog;
using Content.Client.GameTicking;
using Content.Client.Info;
using Content.Client.LateJoin;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Content.Shared.CCVar;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.MainHub;

/// <summary>
/// Full screen main menu shown by ST14HubState right after connecting. Layout follows the main menu of Starship Troopers: Terran Command
/// </summary>
public sealed partial class ST14HubScreen : Control
{
    [Dependency] private IBaseClient _baseClient = default!;
    [Dependency] private IClientConsoleHost _console = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IUriOpener _uriOpener = default!;
    [Dependency] private IUserInterfaceManager _uiManager = default!;

    public static readonly ProtoId<ST14HubScenePrototype>[] Wallpapers =
    {
        "ST14HubOrbit",
        "ST14HubRidge",
        "ST14HubFleet",
        "ST14HubMarauder",
        "ST14HubDesert",
        "ST14HubInferno",
    };

    private const int NewsCount = 3;
    private static readonly TimeSpan NewsInterval = TimeSpan.FromSeconds(8);

    // Bottom row of the original: the big card on the left, then plates every 212.68 px
    private const float RowLeft = 30.76f;
    private const float RowStep = 212.68f;
    private static readonly Vector2 PlateSize = new(187.68f, 70f);

    private readonly ST14AnimatedScene _scene;
    private readonly Label _newsLabel;
    private readonly Label _hourLabel;
    private readonly Label _minuteLabel;
    private readonly Label _dateLabel;
    private readonly Label _tempLabel;
    private readonly Label _statusLabel;
    private readonly ST14HubButton _observeButton;
    private readonly ST14SettingsScreen _settings;
    private readonly ST14HubLoadingScreen _loading;
    private readonly ST14ModeSelectScreen _modes;
    private readonly ST14GalaxyMapScreen _galaxy;
    private readonly ST14SupportScreen _support;

    private int _newsIndex;
    private TimeSpan _newsElapsed;

    public ST14HubScreen()
    {
        IoCManager.InjectDependencies(this);

        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;

        AddChild(new PanelContainer { PanelOverride = ST14HubStyle.Flat(Color.Black) });

        _scene = new ST14AnimatedScene { ScanlineAlpha = 0.3f };
        _scene.SetScenes(Wallpapers);
        AddChild(_scene);

        var canvas = new ST14CanvasLayout { ScaleLabels = true };
        AddChild(canvas);

        // Bottom bar and the status line above it
        canvas.Add(new PanelContainer { PanelOverride = ST14HubStyle.MainBar(_resCache) },
            new Vector2(0, 1), new Vector2(1, 1), Vector2.Zero, new Vector2(0, 93), new Vector2(0.5f, 1));

        _statusLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Italic, 15, Color.White);
        _statusLabel.Align = Label.AlignMode.Center;
        _statusLabel.VAlign = Label.VAlignMode.Center;
        canvas.AddAt(_statusLabel, new Vector2(0.5f, 0.5f), new Vector2(0, 431), new Vector2(827, 32));

        // News banner
        canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "mask_bottom.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = ST14HubStyle.NewsFade,
        }, Vector2.Zero, new Vector2(1, 0), Vector2.Zero, new Vector2(0, 230), new Vector2(0.5f, 0));

        canvas.Add(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "objectivebanner.png"),
            Stretch = TextureRect.StretchMode.Scale,
            ModulateSelfOverride = ST14HubStyle.NewsBar,
        }, Vector2.Zero, new Vector2(1, 0), new Vector2(0, 35), new Vector2(0, 50), new Vector2(0.5f, 0));

        canvas.AddAt(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "news_planet.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        }, Vector2.Zero, new Vector2(12, 60), new Vector2(70, 70), new Vector2(0, 0.5f));

        var newsRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 25,
        };
        var newsTitle = ST14HubStyle.Label(_resCache, Loc.GetString("st14-hub-news-title"), ST14HubFont.Regular, 27, ST14HubStyle.News);
        newsTitle.VAlign = Label.VAlignMode.Center;
        newsRow.AddChild(newsTitle);
        _newsLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 27, ST14HubStyle.News);
        _newsLabel.VAlign = Label.VAlignMode.Center;
        _newsLabel.ClipText = true;
        _newsLabel.HorizontalExpand = true;
        newsRow.AddChild(_newsLabel);
        canvas.Add(newsRow, Vector2.Zero, new Vector2(1, 0), new Vector2(-176.5f, 60), new Vector2(-537, 50), new Vector2(0.5f, 0.5f));

        var moreButton = new ST14HubButton(_resCache, Loc.GetString("st14-hub-news-more"), 25, ST14HubStyle.Orange, ST14HubStyle.News);
        moreButton.OnPressed += _ => _uiManager.GetUIController<ChangelogUIController>().ToggleWindow();
        canvas.AddAt(moreButton, new Vector2(1, 0), new Vector2(-25, 60), new Vector2(400, 70), new Vector2(1, 0.5f));

        // Weather and clock widgets under the banner, every part sits where the original puts it relative to the widget center
        var weather = new ST14CanvasLayout();
        _tempLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 30, ST14HubStyle.Widget);
        _tempLabel.Align = Label.AlignMode.Center;
        _tempLabel.VAlign = Label.VAlignMode.Center;
        weather.AddAt(_tempLabel, new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(160, 46));
        weather.AddAt(new TextureRect
        {
            Texture = ST14HubStyle.Tex(_resCache, "weather_sunny.png"),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            ModulateSelfOverride = ST14HubStyle.Widget,
        }, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(60, 60));
        canvas.AddAt(weather, new Vector2(1, 0), new Vector2(-215, 136.5f), new Vector2(127.6f, 103.3f), new Vector2(1, 0));

        var clock = new ST14CanvasLayout();
        var time = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Align = BoxContainer.AlignMode.Center,
        };
        _hourLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 40, ST14HubStyle.Widget);
        _minuteLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 35, ST14HubStyle.Widget);
        var colon = ST14HubStyle.Label(_resCache, ":", ST14HubFont.Regular, 40, ST14HubStyle.Widget);
        foreach (var part in new[] { _hourLabel, colon, _minuteLabel })
        {
            part.VAlign = Label.VAlignMode.Center;
            time.AddChild(part);
        }
        clock.AddAt(time, new Vector2(0.5f, 0.5f), new Vector2(0, -28.7f), new Vector2(200, 50));
        clock.AddAt(new PanelContainer { PanelOverride = ST14HubStyle.Flat(ST14HubStyle.Widget) },
            new Vector2(0.5f, 0.5f), Vector2.Zero, new Vector2(150, 3.5f));
        _dateLabel = ST14HubStyle.Label(_resCache, string.Empty, ST14HubFont.Regular, 25, ST14HubStyle.Widget);
        _dateLabel.Align = Label.AlignMode.Center;
        _dateLabel.VAlign = Label.VAlignMode.Center;
        clock.AddAt(_dateLabel, new Vector2(0.5f, 0.5f), new Vector2(0, 30.7f), new Vector2(242, 40));
        canvas.AddAt(clock, new Vector2(1, 0), new Vector2(-20.4f, 136.5f), new Vector2(170.6f, 103.3f), new Vector2(1, 0));

        // Social icons above the bottom bar
        AddLinkIcon(canvas, "discord.png", CCVars.InfoLinksDiscord, new Vector2(-84.2f, -298.2f), 125, 0.53f);
        AddLinkIcon(canvas, "twitter.png", CCVars.InfoLinksWebsite, new Vector2(-84.2f, -165.8f), 103, 0.67f);

        // "To battle!" opens the mode selection, then the galaxy map, a landing on Klendathu there leads to the lobby
        var warButton = AddPlate(canvas, 0, "st14-hub-button-war", ST14HubStyle.Blue, ST14HubStyle.BlueText);
        warButton.OnPressed += _ => ShowOverlay(_modes!);

        AddPlate(canvas, 1, "st14-hub-button-character").OnPressed += _ => StartLoading(OpenCharacterSetup);

        _observeButton = AddPlate(canvas, 2, "st14-hub-button-observe");
        _observeButton.OnPressed += _ => new ObserveWarningWindow().OpenCentered();

        AddPlate(canvas, 3, "st14-hub-button-guidebook").OnPressed +=
            _ => _uiManager.GetUIController<GuidebookUIController>().ToggleGuidebook();

        AddPlate(canvas, 4, "st14-hub-rules").OnPressed += _ => new RulesAndInfoWindow().Open();

        // supporter perks and the way to Boosty
        AddPlate(canvas, 5, "st14-hub-button-support", ST14HubStyle.Orange, Color.White).OnPressed += _ => ShowOverlay(_support!);

        var settingsButton = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, "gear.png"), ST14HubStyle.Gold);
        settingsButton.OnPressed += _ => _settings!.Show();
        canvas.AddAt(settingsButton, new Vector2(1, 1), new Vector2(-122.5f, -25), new Vector2(60, 60), new Vector2(1, 1));

        var exitButton = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, "power.png"), ST14HubStyle.Gold)
        {
            PressSound = ST14HubSound.Back,
        };
        exitButton.OnPressed += _ => _console.ExecuteCommand("disconnect");
        canvas.AddAt(exitButton, new Vector2(1, 1), new Vector2(-37.5f, -25), new Vector2(60, 60), new Vector2(1, 1));

        // Settings cover the hub when opened
        _settings = new ST14SettingsScreen { Visible = false };
        _settings.OnClosed += () => _scene.Visible = true;
        _settings.OnOpened += () => _scene.Visible = false;
        AddChild(_settings);

        _modes = new ST14ModeSelectScreen();
        _modes.OnBack += () => ShowOverlay(null);
        _modes.OnGalacticWar += () => ShowOverlay(_galaxy);
        AddChild(_modes);

        _galaxy = new ST14GalaxyMapScreen();
        _galaxy.OnBack += () => ShowOverlay(_modes);
        _galaxy.OnLanding += GoToWar;
        AddChild(_galaxy);

        _support = new ST14SupportScreen();
        _support.OnBack += () => ShowOverlay(null);
        AddChild(_support);

        _loading = new ST14HubLoadingScreen();
        AddChild(_loading);

        ShowNews(0);
        UpdateClock();
    }

    private ST14HubButton AddPlate(ST14CanvasLayout canvas, int index, string loc, Color? tint = null, Color? textColor = null)
    {
        var button = new ST14HubButton(_resCache, Loc.GetString(loc), 21, tint, textColor);
        // the original starts the row with a big "Continue" card, here the plates take its place
        var x = RowLeft + PlateSize.X / 2 + RowStep * index;
        canvas.AddAt(button, new Vector2(0, 1), new Vector2(x, -54), PlateSize);
        return button;
    }

    private void AddLinkIcon(ST14CanvasLayout canvas, string texture, CVarDef<string> cvar, Vector2 position, float size, float alpha)
    {
        var url = _cfg.GetCVar(cvar);
        if (string.IsNullOrEmpty(url))
            return;

        var icon = new ST14HubIconButton(ST14HubStyle.Tex(_resCache, texture), Color.White.WithAlpha(alpha));
        icon.OnPressed += _ => _uriOpener.OpenUri(url);
        canvas.AddAt(icon, new Vector2(1, 1), position, new Vector2(size, size));
    }

    private ClientGameTicker GameTicker => _entMan.System<ClientGameTicker>();

    private ST14HubUIController HubController => _uiManager.GetUIController<ST14HubUIController>();

    /// <summary>
    /// Shows one of the full screen pages above the hub, or the hub itself for null. The hub background is not drawn under them
    /// </summary>
    private void ShowOverlay(Control? overlay)
    {
        _modes.Visible = overlay == _modes;
        _galaxy.Visible = overlay == _galaxy;
        _support.Visible = overlay == _support;
        _scene.Visible = overlay == null;
    }

    /// <summary>
    /// Landing on the galaxy map: into the lobby through the loading screen, straight to the join window if the round is on
    /// </summary>
    private void GoToWar()
    {
        StartLoading(() =>
        {
            HubController.EnterLobby();
            if (GameTicker.IsGameStarted)
                new LateJoinGui().OpenCentered();
        });
    }

    /// <summary>
    /// Covers the hub with the loading screen of the original and runs the switch once it is shown
    /// </summary>
    private void StartLoading(Action then)
    {
        _settings.Close();
        _loading.Start(then);
    }

    private void OpenCharacterSetup()
    {
        HubController.EnterLobby().SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
    }

    private void ShowNews(int index)
    {
        _newsIndex = index % NewsCount;
        _newsLabel.Text = Loc.GetString($"st14-hub-news-{_newsIndex + 1}");
    }

    // Labels are only touched when their value changes: a new text means a new layout pass and new strings,
    // doing that every frame costs time right next to the animated background
    private int _shownMinute = -1;
    private bool? _shownStarted;
    private string? _shownServer;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        var minute = now.Hour * 60 + now.Minute;
        if (minute == _shownMinute)
            return;

        _shownMinute = minute;
        _hourLabel.Text = $"{now.Hour:D2}";
        _minuteLabel.Text = $"{now.Minute:D2}";
        _dateLabel.Text = Loc.GetString("st14-hub-date", ("day", now.Day), ("month", Loc.GetString($"st14-hub-month-{now.Month}")));

        // The original shows a made up forecast as well, keep it stable for the day
        _tempLabel.Text = Loc.GetString("st14-hub-temperature", ("degrees", 18 + now.DayOfYear * 7 % 13));
    }

    private void UpdateStatus()
    {
        var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;
        var started = GameTicker.IsGameStarted;
        if (started == _shownStarted && serverName == _shownServer)
            return;

        _shownStarted = started;
        _shownServer = serverName;
        _statusLabel.Text = Loc.GetString(started ? "st14-hub-status-round" : "st14-hub-status-lobby",
            ("server", serverName));

        _observeButton.Disabled = !started;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!Visible)
            return;

        _newsElapsed += TimeSpan.FromSeconds(args.DeltaSeconds);
        if (_newsElapsed >= NewsInterval)
        {
            _newsElapsed = TimeSpan.Zero;
            ShowNews(_newsIndex + 1);
        }

        UpdateClock();
        UpdateStatus();
    }
}
