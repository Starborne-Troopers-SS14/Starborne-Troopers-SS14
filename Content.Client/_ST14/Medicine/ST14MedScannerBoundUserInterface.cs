using Content.Shared._ST14.Medicine;
using Robust.Client.UserInterface;

namespace Content.Client._ST14.Medicine;

public sealed class ST14MedScannerBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ST14MedScannerWindow? _window;

    public ST14MedScannerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ST14MedScannerWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ST14MedScannerState scanner)
            _window?.Update(scanner);
    }
}
