using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Combat abilities of the castes. The warrior lives in the .Warrior part, the tiger in the .Tiger part
/// </summary>
public sealed partial class ST14BugAbilitySystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedST14HiveSystem _hive = default!;

    private readonly HashSet<Entity<MobStateComponent>> _victims = new();
    private readonly List<(EntityUid? Action, TimeSpan Cooldown)> _cooldowns = new();

    public override void Initialize()
    {
        base.Initialize();

        InitializeWarrior();
        InitializeTiger();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        foreach (var (action, cooldown) in _cooldowns)
        {
            if (!TerminatingOrDeleted(action))
                _actions.SetCooldown(action, cooldown);
        }

        _cooldowns.Clear();

        UpdateWarrior(frameTime);
        UpdateTiger(frameTime);
    }

    /// <summary>
    /// Puts an action on cooldown on the next tick. Set from inside the action's own handler, a cooldown would be
    /// wiped right away by the actions system, which restarts the cooldown of every handled action
    /// </summary>
    private void StartCooldown(EntityUid? action, TimeSpan cooldown)
    {
        _cooldowns.Add((action, cooldown));
    }

    /// <summary>
    /// The mob a click points at: the closest one to the clicked spot that fits, so the player does not have to hit
    /// the sprite exactly
    /// </summary>
    private EntityUid? PickTarget(EntityCoordinates click, Func<EntityUid, bool> valid)
    {
        const float pickRadius = 1.5f;

        _victims.Clear();
        _lookup.GetEntitiesInRange(click, pickRadius, _victims);

        var clicked = _transform.ToMapCoordinates(click);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var mob in _victims)
        {
            if (!valid(mob))
                continue;

            var position = _transform.GetMapCoordinates(mob);
            if (position.MapId != clicked.MapId)
                continue;

            var distance = (position.Position - clicked.Position).LengthSquared();
            if (distance < bestDistance)
            {
                best = mob;
                bestDistance = distance;
            }
        }

        return best;
    }
}
