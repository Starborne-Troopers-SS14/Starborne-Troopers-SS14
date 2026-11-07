using System.Linq;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// What the wounds and the drugs do to the body on both sides, so walking and aiming stay predicted:
/// a broken leg limps, pain slows and shakes the hands, a broken arm cannot hold a rifle steady,
/// a stimulant drives the legs and steadies the aim, its crash and ketamine do the opposite
/// </summary>
public sealed partial class ST14WoundEffectsSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14WoundsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ST14WoundsComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnGunRefresh);
    }

    private void OnState(Entity<ST14WoundsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Refresh(ent);
        var ev = new ST14WoundsChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    public static int Fractures(ST14WoundsComponent comp, bool legs, bool splinted)
    {
        return comp.Wounds.Count(w => w.Kind == ST14WoundKind.Fracture && w.Splinted == splinted &&
                                      (legs ? ST14Zones.IsLeg(w.Zone) : ST14Zones.IsArm(w.Zone)));
    }

    public static int Malunions(ST14WoundsComponent comp, bool legs)
    {
        return comp.Wounds.Count(w => w.Kind == ST14WoundKind.Malunion && (legs ? ST14Zones.IsLeg(w.Zone) : ST14Zones.IsArm(w.Zone)));
    }

    public static float SpeedOf(ST14WoundsComponent comp)
    {
        var speed = MathF.Pow(0.68f, Fractures(comp, true, false)) * MathF.Pow(0.9f, Fractures(comp, true, true))
                    * MathF.Pow(0.88f, Malunions(comp, true));

        // pain drags the feet, unless a stimulant drowns it out
        if (comp.Pain > 55f && comp.Stim < 0.25f)
            speed *= MathF.Max(0.6f, 1f - (comp.Pain - 55f) / 110f);

        if (comp.Blood < 0.75f)
            speed *= Math.Clamp(0.55f + (comp.Blood - 0.4f) / 0.35f * 0.45f, 0.55f, 1f);

        speed *= 1f + 0.18f * comp.Stim;
        speed *= 1f - 0.35f * comp.Crash;
        speed *= 1f - 0.5f * comp.Dissociation;
        speed *= 1f - 0.35f * comp.Toxin;
        speed *= 1f - 0.12f * comp.Fever;
        return Math.Clamp(speed, 0.15f, 1.3f);
    }

    /// <summary>
    /// Degrees the aim wanders on top of the gun's own spread
    /// </summary>
    public static float AimPenaltyOf(ST14WoundsComponent comp)
    {
        var penalty = 7f * Fractures(comp, false, false) + 2f * Fractures(comp, false, true) + 3f * Malunions(comp, false);
        if (comp.Pain > 35f)
            penalty += (comp.Pain - 35f) * 0.15f * (1f - 0.6f * comp.Adrenaline);

        penalty += 6f * MathF.Max(0f, comp.Sedation - 0.5f);
        penalty += 10f * comp.Dissociation + 5f * comp.Crash + 6f * comp.Toxin + 3f * comp.Fever;
        penalty -= 2f * comp.Stim + comp.Steady;
        return MathF.Max(0f, penalty);
    }

    private void OnRefreshSpeed(Entity<ST14WoundsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = SpeedOf(ent.Comp);
        args.ModifySpeed(speed, speed);
    }

    private void OnGunRefresh(Entity<GunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (!_container.TryGetContainingContainer((ent.Owner, null, null), out var container) ||
            !TryComp<ST14WoundsComponent>(container.Owner, out var wounds))
        {
            return;
        }

        var penalty = AimPenaltyOf(wounds);
        if (penalty <= 0f)
            return;

        args.MinAngle += Angle.FromDegrees(penalty * 0.6f);
        args.MaxAngle += Angle.FromDegrees(penalty * 1.5f);
    }

    /// <summary>
    /// Walks and aims again when the wounds or the drugs changed enough to matter
    /// </summary>
    public void Refresh(Entity<ST14WoundsComponent> ent)
    {
        var speedKey = (int) (SpeedOf(ent.Comp) * 50f);
        if (speedKey != ent.Comp.SpeedKey)
        {
            ent.Comp.SpeedKey = speedKey;
            _speed.RefreshMovementSpeedModifiers(ent.Owner);
        }

        var aimKey = (int) AimPenaltyOf(ent.Comp);
        if (aimKey == ent.Comp.AimKey)
            return;

        ent.Comp.AimKey = aimKey;
        foreach (var held in _hands.EnumerateHeld(ent.Owner))
        {
            if (HasComp<GunComponent>(held))
                _gun.RefreshModifiers(held);
        }
    }
}

/// <summary>
/// The wounds of a body came in from the server: the marks on it can be redrawn
/// </summary>
[ByRefEvent]
public record struct ST14WoundsChangedEvent;
