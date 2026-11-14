using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Shared.GameTicking;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using RoundEndPlayerInfo = Content.Shared.GameTicking.RoundEndMessageEvent.RoundEndPlayerInfo;

namespace Content.Client._ST14.RoundEnd;

/// <summary>
/// End credits of a round instead of the summary window: the lines roll from the bottom of the screen to the top, one
/// per player with the role, the character name and the ckey. Supporters go first, the highest tier at the top, each
/// tier in its colour and a bigger font; then everyone else. The skip button closes it, the faster button speeds it up,
/// the summary window stays on its key
/// </summary>
public sealed class ST14RoundCreditsScreen : PanelContainer
{
    private const float Speed = 55f;
    private const float FastSpeed = 260f;

    // font size of a line of every tier, index 0 is a player without a subscription
    private static readonly int[] TierSizes = { 15, 17, 19, 21, 24 };

    private readonly BoxContainer _column;
    private readonly Control _viewport;
    private float _offset;
    private bool _started;
    private bool _fast;

    public event Action? OnClosed;

    public ST14RoundCreditsScreen(IResourceCache cache, RoundEndMessageEvent message)
    {
        MouseFilter = MouseFilterMode.Stop;


        // the clipping area the column rolls through
        _viewport = new ST14ClipControl { HorizontalExpand = true, VerticalExpand = true };
        AddChild(_viewport);

        _column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        _viewport.AddChild(_column);
        // below the screen until the first frame lays it out
        LayoutContainer.SetPosition(_column, new Vector2(0, 100000));

        Fill(cache, message);

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 12,
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(0, 0, 32, 28),
        };
        var fast = new ST14HubButton(cache, Loc.GetString("st14-credits-faster"), 17) { ToggleMode = true, MinSize = new Vector2(190, 54) };
        fast.OnToggled += args => _fast = args.Pressed;
        buttons.AddChild(fast);
        var skip = new ST14HubButton(cache, Loc.GetString("st14-credits-skip"), 17) { MinSize = new Vector2(190, 54) };
        skip.OnPressed += _ => Close();
        buttons.AddChild(skip);
        AddChild(buttons);
    }

    private void Fill(IResourceCache cache, RoundEndMessageEvent message)
    {
        void Add(Control control, float top = 0)
        {
            control.HorizontalAlignment = HAlignment.Center;
            control.Margin = new Thickness(0, top, 0, 0);
            _column.AddChild(control);
        }

        Add(ST14HubStyle.Label(cache, Loc.GetString("st14-credits-title"), ST14HubFont.Bold, 34, ST14HubStyle.GoldLight, true));
        Add(ST14HubStyle.Label(cache, Loc.GetString("st14-credits-round", ("id", message.RoundId),
            ("duration", $"{(int) message.RoundDuration.TotalHours}:{message.RoundDuration.Minutes:00}:{message.RoundDuration.Seconds:00}")),
            ST14HubFont.Medium, 16, ST14HubStyle.BlueText));

        var players = Players(message.AllPlayersEndInfo);
        var supporters = players.Where(p => p.ST14SupporterTier > 0).ToList();
        var others = players.Where(p => p.ST14SupporterTier <= 0).ToList();

        if (supporters.Count > 0)
        {
            Add(Heading(cache, Loc.GetString("st14-credits-supporters"), ST14SupporterStatus.TierColors[4]), 60);
            foreach (var player in supporters)
            {
                Add(Line(cache, player), 4);
            }
        }

        if (others.Count > 0)
        {
            Add(Heading(cache, Loc.GetString("st14-credits-players"), ST14HubStyle.GoldLight), 60);
            foreach (var player in others)
            {
                Add(Line(cache, player));
            }
        }

        Add(ST14HubStyle.Label(cache, Loc.GetString("st14-credits-thanks"), ST14HubFont.SemiBold, 22, ST14HubStyle.GoldLight, true), 90);
    }

    /// <summary>
    /// One entry per player: the role they played rather than the observer ghost, supporters by tier then by name
    /// </summary>
    private static List<RoundEndPlayerInfo> Players(RoundEndPlayerInfo[] all)
    {
        return all
            .Where(p => !p.Observer)
            .GroupBy(p => p.PlayerOOCName)
            .Select(g => g.First())
            .OrderByDescending(p => Math.Clamp(p.ST14SupporterTier, 0, 4))
            .ThenBy(p => p.PlayerOOCName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Control Heading(IResourceCache cache, string text, Color color)
    {
        return ST14HubStyle.Label(cache, text.ToUpperInvariant(), ST14HubFont.Bold, 20, color, true);
    }

    private static Control Line(IResourceCache cache, RoundEndPlayerInfo player)
    {
        var tier = Math.Clamp(player.ST14SupporterTier, 0, 4);
        var size = TierSizes[tier];
        var supporter = tier > 0;
        var color = supporter ? ST14SupporterStatus.TierColors[tier] : Color.FromHex("#D8D8D8");
        var dim = supporter ? color.WithAlpha(0.75f) : Color.FromHex("#8E8E8E");

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 14,
        };

        if (supporter)
        {
            // the rank of the subscription in front, the glow is a wide outline of the tier colour
            row.AddChild(ST14HubStyle.Label(cache, $"★ {ST14SupporterStatus.Numerals[tier]}", ST14HubFont.Bold, size, color, true));
        }

        row.AddChild(ST14HubStyle.Label(cache, RoleName(player.Role), ST14HubFont.Regular, size, dim));

        var name = ST14HubStyle.Label(cache, player.PlayerICName ?? "?", supporter ? ST14HubFont.Bold : ST14HubFont.SemiBold, size, color);
        if (supporter)
        {
            name.OutlineThicknessOverride = tier >= 3 ? 3f : 2f;
            name.OutlineColorOverride = color.WithAlpha(0.28f);
        }
        row.AddChild(name);

        row.AddChild(ST14HubStyle.Label(cache, $"({player.PlayerOOCName})", ST14HubFont.Italic, Math.Max(13, size - 2), dim));
        return row;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIClick)
            args.Handle();
    }

    /// <summary>
    /// The server may send the locale key of a job rather than its name, the client puts it into its own language
    /// </summary>
    private static string RoleName(string role)
    {
        return Loc.TryGetString(role, out var name) ? name : role;
    }

    /// <summary>
    /// An almost opaque backdrop under everything, the game must not show through the credits
    /// </summary>
    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(PixelSizeBox, Backdrop);
        base.Draw(handle);
    }

    private static readonly Color Backdrop = Color.FromHex("#050505F2");

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var column = _column.DesiredSize.Y;
        var height = _viewport.Size.Y;
        if (column <= 0 || height <= 0)
            return;

        // the first line comes in from under the bottom edge
        if (!_started)
        {
            _started = true;
            _offset = height;
        }

        _offset -= (_fast ? FastSpeed : Speed) * args.DeltaSeconds;
        LayoutContainer.SetPosition(_column, new Vector2((_viewport.Size.X - _column.DesiredSize.X) / 2, _offset));

        // all rolled out of the top; removing a control while the UI walks its frame update breaks the walk, so later
        if (_offset < -column)
            UserInterfaceManager.DeferAction(Close);
    }

    public void Close()
    {
        if (Disposed || Parent == null)
            return;

        Orphan();
        OnClosed?.Invoke();
    }
}

/// <summary>
/// A layout container that does not draw its children outside its own rect
/// </summary>
public sealed class ST14ClipControl : LayoutContainer
{
    public ST14ClipControl()
    {
        RectClipContent = true;
    }
}
