using Content.Shared.Movement.Systems;

namespace Content.Shared._ST14.EnergyFence;

/// <summary>
/// Shared part of the fence: the slowdown of a shocked bug, so its movement predicts right on the client
/// </summary>
public sealed partial class ST14SharedEnergyFenceSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14FenceShockedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ST14FenceShockedComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<ST14FenceShockedComponent, ComponentStartup>(OnChanged);
        SubscribeLocalEvent<ST14FenceShockedComponent, ComponentShutdown>(OnChanged);
    }

    private void OnRefreshSpeed(Entity<ST14FenceShockedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }

    private void OnHandleState(Entity<ST14FenceShockedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnChanged<T>(Entity<ST14FenceShockedComponent> ent, ref T args)
    {
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    /// <summary>
    /// Sets the slowdown of a shocked bug, refreshing its speed only when the number really changes
    /// </summary>
    public void SetSpeed(Entity<ST14FenceShockedComponent> ent, float depth, float modifier)
    {
        var changed = false;
        if (MathF.Abs(ent.Comp.Depth - depth) > 0.04f)
        {
            ent.Comp.Depth = depth;
            changed = true;
        }

        if (MathF.Abs(ent.Comp.SpeedModifier - modifier) > 0.02f)
        {
            ent.Comp.SpeedModifier = modifier;
            changed = true;
            _speed.RefreshMovementSpeedModifiers(ent.Owner);
        }

        if (changed)
            Dirty(ent);
    }
}
