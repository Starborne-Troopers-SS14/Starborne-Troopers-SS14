using Content.Server.Power.Components;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Audio;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Robust.Shared.Audio.Systems;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// Field generator: an MV power supplier switched on and off by hand. Portable: unanchor it, carry it, anchor it again
/// </summary>
public sealed partial class ST14FieldGeneratorSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14FieldGeneratorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14FieldGeneratorComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14FieldGeneratorComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<ST14FieldGeneratorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<ST14FieldGeneratorComponent> ent, ref MapInitEvent args)
    {
        UpdateState(ent);
    }

    private void OnAnchorChanged(Entity<ST14FieldGeneratorComponent> ent, ref AnchorStateChangedEvent args)
    {
        // Before map init the looks are set by OnMapInit
        if (LifeStage(ent) < EntityLifeStage.MapInitialized)
            return;

        UpdateState(ent);
    }

    private void OnActivate(Entity<ST14FieldGeneratorComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        SetOn(ent, !ent.Comp.On);
    }

    private void OnExamined(Entity<ST14FieldGeneratorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.On ? "st14-field-generator-examine-on" : "st14-field-generator-examine-off"));
    }

    public void SetOn(Entity<ST14FieldGeneratorComponent> ent, bool on)
    {
        if (ent.Comp.On == on)
            return;

        ent.Comp.On = on;
        _audio.PlayPvs(ent.Comp.ToggleSound, ent);
        UpdateState(ent);
    }

    private void UpdateState(Entity<ST14FieldGeneratorComponent> ent)
    {
        var anchored = Transform(ent).Anchored;
        var running = anchored && ent.Comp.On;

        if (TryComp<PowerSupplierComponent>(ent, out var supplier))
            supplier.Enabled = running;

        _ambient.SetAmbience(ent, running);
        _appearance.SetData(ent, ST14FieldGeneratorVisuals.State,
            !anchored ? ST14FieldGeneratorState.Unanchored
            : ent.Comp.On ? ST14FieldGeneratorState.On
            : ST14FieldGeneratorState.Off);
    }
}
