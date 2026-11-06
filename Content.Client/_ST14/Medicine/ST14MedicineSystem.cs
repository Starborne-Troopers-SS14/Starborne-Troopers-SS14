using Content.Shared._ST14.Input;
using Content.Shared._ST14.Medicine;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// The heartbeat key and the monitors of <see cref="ST14MedicineOverlay"/>
/// </summary>
public sealed partial class ST14MedicineSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;

    private ST14MedicineOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay = new ST14MedicineOverlay();
        _overlays.AddOverlay(_overlay);

        CommandBinds.Builder
            .Bind(ST14KeyFunctions.Heartbeat, InputCmdHandler.FromDelegate(_ => Press()))
            .Register<ST14MedicineSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<ST14MedicineSystem>();
        _overlays.RemoveOverlay(_overlay);
    }

    private void Press()
    {
        if (HasComp<ST14DyingComponent>(_player.LocalEntity))
            RaiseNetworkEvent(new ST14HeartbeatPressEvent());
    }
}
