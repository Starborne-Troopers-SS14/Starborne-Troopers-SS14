using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._ST14.CCVar;
using Content.Shared.Alert;
using Content.Shared.Atmos.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.Atmos;

/// <summary>
/// Switches atmospherics off for Starborne Troopers (cvar st14.atmospherics): no grid is simulated, so gases never spread,
/// and mobs neither breathe nor take pressure damage, so nobody suffocates on a planet without air.
/// Turning the cvar back on simulates the grids again and gives every mob its breathing and pressure back
/// </summary>
public sealed partial class ST14AtmosToggleSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private AlertsSystem _alerts = default!;

    private bool _enabled = true;

    // what spawned while atmospherics is off, switched off on the next tick once it is set up
    private readonly HashSet<EntityUid> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        // no subscription to the components' own events: the atmos and body systems already own them
        EntityManager.ComponentAdded += OnComponentAdded;
        Subs.CVar(_cfg, ST14CVars.Atmospherics, OnToggled, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        EntityManager.ComponentAdded -= OnComponentAdded;
    }

    private void OnComponentAdded(AddedComponentEventArgs args)
    {
        if (_enabled)
            return;

        if (args.BaseArgs.Component is GridAtmosphereComponent or RespiratorComponent or BarotraumaComponent)
            _pending.Add(args.BaseArgs.Owner);
    }

    private void OnToggled(bool enabled)
    {
        if (_enabled == enabled)
            return;

        _enabled = enabled;
        _pending.Clear();

        var grids = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (grids.MoveNext(out var uid, out var atmos))
        {
            _atmos.SetAtmosphereSimulation((uid, atmos), enabled);
        }

        if (enabled)
        {
            RestoreBreathing();
            return;
        }

        var respirators = EntityQueryEnumerator<RespiratorComponent>();
        while (respirators.MoveNext(out var uid, out _))
        {
            _pending.Add(uid);
        }

        var barotrauma = EntityQueryEnumerator<BarotraumaComponent>();
        while (barotrauma.MoveNext(out var uid, out _))
        {
            _pending.Add(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending)
        {
            if (!TerminatingOrDeleted(uid))
                SwitchOff(uid);
        }

        _pending.Clear();
    }

    /// <summary>
    /// Stops a grid from being simulated, or takes breathing and pressure damage away from a mob
    /// </summary>
    private void SwitchOff(EntityUid uid)
    {
        if (TryComp<GridAtmosphereComponent>(uid, out var atmos))
            _atmos.SetAtmosphereSimulation((uid, atmos), false);

        if (HasComp<RespiratorComponent>(uid))
        {
            // the lungs take down the suffocation icon, but only when told the suffocating stopped
            var ev = new StopSuffocatingEvent();
            RaiseLocalEvent(uid, ref ev);
            RemComp<RespiratorComponent>(uid);
        }

        if (TryComp<BarotraumaComponent>(uid, out var barotrauma))
        {
            // the pressure icon stays on screen when the component goes, take it down too
            _alerts.ClearAlertCategory(uid, barotrauma.PressureAlertCategory);
            RemComp(uid, barotrauma);
        }
    }

    /// <summary>
    /// Gives breathing and pressure back to every mob whose prototype has them
    /// </summary>
    private void RestoreBreathing()
    {
        var respirator = _factory.GetComponentName<RespiratorComponent>();
        var barotrauma = _factory.GetComponentName<BarotraumaComponent>();

        var query = EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype is not { } proto)
                continue;

            var missing = new ComponentRegistry();
            foreach (var name in new[] { respirator, barotrauma })
            {
                if (proto.Components.TryGetValue(name, out var entry) && !HasComp(uid, entry.Component.GetType()))
                    missing.Add(name, entry);
            }

            if (missing.Count > 0)
                EntityManager.AddComponents(uid, missing, false);
        }
    }
}
