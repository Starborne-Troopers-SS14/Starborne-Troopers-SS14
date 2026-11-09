using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.MainHub;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Lobby;

/// <summary>
/// Over the whole lobby while the player has no side in the war over the region: what the war is, where, since when and
/// why, and the two sides with what playing each of them means. It does not go away until a side is chosen,
/// and the choice needs a second press, it holds until the region is taken whole
/// </summary>
public sealed class ST14SideChoiceScreen : PanelContainer
{
    private const float BoardWidth = 1240;
    private const float Gap = 14;

    private readonly IResourceCache _cache;
    private readonly ST14SideClientSystem _sides;
    private readonly Button _federation;
    private readonly Button _arachnids;
    private ST14GalaxyFaction? _confirming;

    public ST14SideChoiceScreen(IResourceCache cache, ST14SideClientSystem sides)
    {
        _cache = cache;
        _sides = sides;
        PanelOverride = ST14HubStyle.Flat(Color.FromHex("#07090B").WithAlpha(0.985f));
        MouseFilter = MouseFilterMode.Stop;

        var center = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 14,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            SetWidth = BoardWidth,
            Margin = new Thickness(24),
        };
        AddChild(center);

        var title = ST14HubStyle.Label(cache, Loc.GetString("st14-side-choice-title").ToUpperInvariant(), ST14HubFont.Black, 40, ST14HubStyle.GoldLight, shadow: true);
        title.Align = Label.AlignMode.Center;
        center.AddChild(title);

        var lore = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = (int) Gap };
        foreach (var key in new[] { "what", "where", "when", "why" })
        {
            lore.AddChild(Fact(Loc.GetString($"st14-side-lore-{key}-title"), Loc.GetString($"st14-side-lore-{key}")));
        }

        center.AddChild(lore);

        var columns = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = (int) Gap };
        _federation = Side(columns, ST14GalaxyFaction.Federation, "emblem_federation.png", "federation", Color.FromHex("#5B97BC"));
        _arachnids = Side(columns, ST14GalaxyFaction.Arachnid, "emblem_hive.png", "arachnids", Color.FromHex("#C9573C"));
        center.AddChild(columns);

        var note = ST14HubStyle.Label(cache, Loc.GetString("st14-side-choice-note"), ST14HubFont.Regular, 15, ST14LobbyStyle.Text);
        note.Align = Label.AlignMode.Center;
        center.AddChild(note);

        Update();
    }

    private Control Fact(string title, string text)
    {
        // fixed widths: a rich text label wraps only inside a width it knows
        var box = new PanelContainer
        {
            SetWidth = (BoardWidth - Gap * 3) / 4,
            PanelOverride = ST14HubStyle.Flat(Color.FromHex("#12161B").WithAlpha(0.9f), ST14HubStyle.Gold.WithAlpha(0.35f), 1),
        };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, Margin = new Thickness(12, 8) };
        column.AddChild(ST14HubStyle.Label(_cache, title.ToUpperInvariant(), ST14HubFont.Bold, 15, ST14HubStyle.Gold));
        column.AddChild(Rich(text, 14));
        box.AddChild(column);
        return box;
    }

    private Button Side(BoxContainer parent, ST14GalaxyFaction side, string emblem, string key, Color accent)
    {
        var frame = new PanelContainer
        {
            SetWidth = (BoardWidth - Gap) / 2,
            PanelOverride = ST14HubStyle.Flat(Color.FromHex("#0E1216").WithAlpha(0.95f), accent.WithAlpha(0.8f), 2),
        };
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(18, 14) };

        var head = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 14 };
        head.AddChild(new TextureRect
        {
            Texture = ST14HqArt.Tex(_cache, emblem),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            SetSize = new Vector2(88, 88),
        });
        var names = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalAlignment = VAlignment.Center };
        names.AddChild(ST14HubStyle.Label(_cache, Loc.GetString($"st14-side-{key}-name").ToUpperInvariant(), ST14HubFont.Black, 28, accent, shadow: true));
        names.AddChild(ST14HubStyle.Label(_cache, Loc.GetString($"st14-side-{key}-motto"), ST14HubFont.Medium, 15, ST14HubStyle.GoldLight));
        head.AddChild(names);
        column.AddChild(head);

        column.AddChild(Rich(Loc.GetString($"st14-side-{key}-lore"), 14));
        column.AddChild(ST14HubStyle.Label(_cache, Loc.GetString("st14-side-choice-play").ToUpperInvariant(), ST14HubFont.Bold, 14, ST14HubStyle.Gold));
        column.AddChild(Rich(Loc.GetString($"st14-side-{key}-play"), 14));

        var button = new Button { MinHeight = 46, Margin = new Thickness(0, 6, 0, 0) };
        button.Label.FontOverride = ST14HubStyle.Font(_cache, ST14HubFont.Bold, 17);
        button.OnPressed += _ => Pick(side);
        column.AddChild(button);

        frame.AddChild(column);
        parent.AddChild(frame);
        return button;
    }

    private RichTextLabel Rich(string text, int size)
    {
        var message = new FormattedMessage();
        message.PushColor(ST14LobbyStyle.Text);
        message.AddText(text);
        message.Pop();
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(message);
        return label;
    }

    private void Pick(ST14GalaxyFaction side)
    {
        if (_confirming != side)
        {
            _confirming = side;
            Update();
            return;
        }

        _confirming = null;
        _sides.Choose(side);
        Update();
    }

    /// <summary>
    /// Shown only when the server said the player has no side
    /// </summary>
    public void Update()
    {
        Visible = _sides.Known && _sides.Side == null;
        _federation.Text = Text(ST14GalaxyFaction.Federation, "st14-side-pick-federation");
        _arachnids.Text = Text(ST14GalaxyFaction.Arachnid, "st14-side-pick-arachnids");
    }

    private string Text(ST14GalaxyFaction side, string key)
    {
        var text = Loc.GetString(key);
        return (_confirming == side ? Loc.GetString("st14-side-confirm", ("side", text)) : text).ToUpperInvariant();
    }
}
