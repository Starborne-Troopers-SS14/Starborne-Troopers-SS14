using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Shared._ST14.CCVar;
using Content.Shared.Alert;
using Content.Shared.Atmos.Components;
using Robust.Shared.Configuration;

namespace Content.Server._ST14.Atmos;

/// <summary>
/// Switches atmospherics off for Starborne Troopers (cvar st14.atmospherics): no grid is simulated, so gases never spread,
/// and mobs neither breathe nor take pressure damage, so nobody suffocates on a planet without air.
/// The mobs lose those parts for good; turning the cvar back on only affects what spawns afterwards
/// </summary>
public sealed partial class ST14AtmosToggleSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private AlertsSystem _alerts = default!;

    // grids and breathers keep spawning, they are switched off in batches instead of one by one
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);

    private bool _enabled;
    private TimeSpan _sweepTimer;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, ST14CVars.Atmospherics, OnToggled, true);
    }

    private void OnToggled(bool enabled)
    {
        _enabled = enabled;

        var query = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (query.MoveNext(out var uid, out var atmos))
        {
            _atmos.SetAtmosphereSimulation((uid, atmos), enabled);
        }

        if (!enabled)
            Sweep();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_enabled)
            return;

        _sweepTimer -= TimeSpan.FromSeconds(frameTime);
        if (_sweepTimer > TimeSpan.Zero)
            return;

        _sweepTimer = SweepInterval;
        Sweep();
    }

    /// <summary>
    /// Stops new grids from being simulated and takes breathing and pressure damage away from every mob that still has them
    /// </summary>
    private void Sweep()
    {
        var grids = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (grids.MoveNext(out var uid, out var atmos))
        {
            if (atmos.Simulated)
                _atmos.SetAtmosphereSimulation((uid, atmos), false);
        }

        var respirators = EntityQueryEnumerator<RespiratorComponent>();
        while (respirators.MoveNext(out var uid, out _))
        {
            RemCompDeferred<RespiratorComponent>(uid);
        }

        var barotrauma = EntityQueryEnumerator<BarotraumaComponent>();
        while (barotrauma.MoveNext(out var uid, out var comp))
        {
            // the pressure icon stays on screen when the component goes, take it down too
            _alerts.ClearAlertCategory(uid, comp.PressureAlertCategory);
            RemCompDeferred<BarotraumaComponent>(uid);
        }
    }
}
