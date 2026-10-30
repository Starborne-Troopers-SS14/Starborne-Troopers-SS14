using Content.Shared._ST14.Hive;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._ST14.Hive;

[UsedImplicitly]
public sealed class ST14HiveEvolutionBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ST14HiveEvolutionWindow? _window;

    public ST14HiveEvolutionBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ST14HiveEvolutionWindow>();
        _window.OnEvolve += caste => SendMessage(new ST14HiveEvolveMessage(caste));
        _window.OnOpenTab += tab => SendMessage(new ST14HiveOpenTabMessage(tab));
        _window.OnOpenSpecies += caste => SendMessage(new ST14HiveOpenSpeciesMessage(caste));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ST14HiveEvolutionState cast)
            _window?.UpdateState(cast);
    }
}
