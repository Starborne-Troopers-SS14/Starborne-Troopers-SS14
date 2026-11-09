using Content.Server.Body.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Alert;
using Content.Shared.Temperature.Components;
using Robust.Shared.Player;

namespace Content.Server._ST14.Medicine;

/// <summary>
/// The war has no body heat: whatever body a player takes, trooper or bug, loses its temperature, so nobody freezes,
/// gets a heatstroke or sees a thermometer on the screen
/// </summary>
public sealed partial class ST14NoTemperatureSystem : EntitySystem
{
    [Dependency] private AlertsSystem _alerts = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        Strip(args.Entity);
    }

    public void Strip(EntityUid uid)
    {
        RemCompDeferred<TemperatureComponent>(uid);
        RemCompDeferred<TemperatureDamageComponent>(uid);
        RemCompDeferred<TemperatureSpeedComponent>(uid);
        RemCompDeferred<ThermalRegulatorComponent>(uid);
        _alerts.ClearAlertCategory(uid, TemperatureSystem.TemperatureAlertCategory);
    }
}
