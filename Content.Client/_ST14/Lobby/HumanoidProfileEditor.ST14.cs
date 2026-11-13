using System.Linq;
using Content.Client._ST14.MainHub;
using Content.Shared._ST14.MainHub;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>
/// ST14 part of the character editor. The war side picked before the lobby decides what is made here: a trooper of the
/// Federation or a bug of the hive (the bug species), with only that side's jobs. A bug has no name of its own: the
/// callsign of its caste and a number, "Воин-394". The antagonist tab is gone, bugs do not get the rows and tabs that only fit a human
/// </summary>
public sealed partial class HumanoidProfileEditor
{
    private const int ST14AntagTab = 2;
    private const int ST14TraitsTab = 3;
    private const int ST14MarkingsTab = 4;

    private BoxContainer? _st14BugNameRow;
    private LineEdit? _st14Number;
    private bool _st14Updating;

    private bool ST14IsBug => Profile != null && ST14CharacterSide.IsBug(Profile.Species);

    private static Robust.Shared.Random.IRobustRandom ST14Random => IoCManager.Resolve<Robust.Shared.Random.IRobustRandom>();

    /// <summary>
    /// Callsign and number of a bug name, shown instead of the name field for bugs
    /// </summary>
    public Control ST14BugNameRow => _st14BugNameRow ??= ST14BuildBugNameRow();

    private BoxContainer ST14BuildBugNameRow()
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6, HorizontalExpand = true };

        // the callsign comes from the caste in the round, the lobby shows the warrior one
        row.AddChild(new Label { Text = $"{Loc.GetString(ST14CharacterSide.DefaultCallsign)} -", VerticalAlignment = VAlignment.Center });

        _st14Number = new LineEdit
        {
            MinSize = new System.Numerics.Vector2(80, 32),
            HorizontalExpand = true,
            IsValid = text => text.Length <= 3 && text.All(char.IsDigit),
        };
        _st14Number.OnTextChanged += _ => ST14ComposeBugName();
        row.AddChild(_st14Number);

        var random = new Button { Text = Loc.GetString("st14-bug-name-random"), MinHeight = 32 };
        random.OnPressed += _ =>
        {
            if (Profile != null)
                ST14SetBugName(ST14CharacterSide.RandomBugName(ST14Random));
        };
        row.AddChild(random);

        // the side is picked in the lobby while the editor may already be built
        _entManager.System<ST14SideClientSystem>().Changed += () =>
        {
            if (!Disposed)
                ST14UpdateSideControls();
        };

        // until something moves it, the row sits right under the name field
        if (NameEdit.Parent is { } parent)
        {
            parent.AddChild(row);
            row.SetPositionInParent(NameEdit.GetPositionInParent() + 1);
        }

        return row;
    }

    private void ST14ComposeBugName()
    {
        if (_st14Updating || _st14Number == null)
            return;

        if (!int.TryParse(_st14Number.Text, out var number) || number < 1)
            return;

        var name = ST14CharacterSide.ProfileBugName(number);
        NameEdit.Text = name;
        SetName(name);
    }

    private void ST14SetBugName(string name)
    {
        NameEdit.Text = name;
        SetName(name);
        ST14ShowBugName();
    }

    /// <summary>
    /// Fills the number from the profile name, a name that is not a bug name gets a random number
    /// </summary>
    private void ST14ShowBugName()
    {
        if (Profile == null || _st14Number == null)
            return;

        if (!ST14CharacterSide.TryParseBugName(Profile.Name, out var number))
        {
            ST14SetBugName(ST14CharacterSide.RandomBugName(ST14Random));
            return;
        }

        _st14Updating = true;
        _st14Number.Text = $"{number}";
        _st14Updating = false;
    }

    /// <summary>
    /// The species was switched: drop the job wishes of the other side and update the form
    /// </summary>
    private void ST14OnSpeciesChanged()
    {
        if (Profile == null)
            return;

        var species = Profile.Species;
        var kept = Profile.JobPriorities.Where(p => ST14CharacterSide.Fits(_prototypeManager, species, p.Key)).ToList();
        if (kept.Count != Profile.JobPriorities.Count)
            Profile = Profile.WithJobPriorities(kept);

        // a trooper made from a bug should not keep the bug's number for a name
        if (!ST14IsBug && ST14CharacterSide.TryParseBugName(Profile.Name, out _))
        {
            var name = HumanoidCharacterProfile.GetName(species, Profile.Gender);
            Profile = Profile.WithName(name);
            NameEdit.Text = name;
        }

        ST14UpdateSideControls();
    }

    /// <summary>
    /// The species the war side asks for, null while the side is not known
    /// </summary>
    private string? ST14SideSpecies()
    {
        var sides = _entManager.System<ST14SideClientSystem>();
        if (sides.Side is not { } side)
            return null;

        return side == ST14GalaxyFaction.Arachnid ? ST14CharacterSide.BugSpecies.Id : (string) HumanoidCharacterProfile.DefaultSpecies;
    }

    /// <summary>
    /// Shows the rows and tabs that fit the side of the character, and turns it to the war side of the player
    /// </summary>
    private void ST14UpdateSideControls()
    {
        // the side is chosen before the lobby: the species follows it and cannot be picked by hand
        var sideSpecies = ST14SideSpecies();
        if (SpeciesButton.Parent is { } speciesRow)
            speciesRow.Visible = sideSpecies == null;

        if (sideSpecies != null && Profile != null && Profile.Species != sideSpecies)
        {
            SetSpecies(sideSpecies);
            return;
        }

        var human = !ST14IsBug;

        foreach (var control in new Control[] { AgeEdit, SexButton, VoiceButton, PronounsButton, ShowClothes })
        {
            if (control.Parent is { } row)
                row.Visible = human;
        }

        if (EyeColorPicker.Parent is { } eyes)
            eyes.Visible = human;

        if (Skin.Parent is { } skin)
            skin.Visible = human;

        NameEdit.Visible = human;
        ST14BugNameRow.Visible = !human;
        if (!human)
            ST14ShowBugName();

        TabContainer.SetTabVisible(ST14AntagTab, false);
        TabContainer.SetTabVisible(ST14TraitsTab, human);
        TabContainer.SetTabVisible(ST14MarkingsTab, human);

        if (!TabContainer.GetTabVisible(TabContainer.CurrentTab))
            TabContainer.CurrentTab = 0;
    }
}
