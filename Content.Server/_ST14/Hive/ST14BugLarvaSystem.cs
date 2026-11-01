using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using System.Linq;
using Content.Server.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Larvae eating corpses for the biomass they need to evolve
/// </summary>
public sealed partial class ST14BugLarvaSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ServerInventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private ST14HiveSystem _hive = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14BiomassEaterComponent, MapInitEvent>(OnEaterMapInit);
        SubscribeLocalEvent<ST14BiomassEaterComponent, ComponentShutdown>(OnEaterShutdown);
        SubscribeLocalEvent<ST14BiomassEaterComponent, ST14EatCorpseActionEvent>(OnEatAction);
        SubscribeLocalEvent<ST14BiomassEaterComponent, ST14EatCorpseDoAfterEvent>(OnEatDoAfter);
    }

    private void OnEaterMapInit(Entity<ST14BiomassEaterComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnEaterShutdown(Entity<ST14BiomassEaterComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnEatAction(Entity<ST14BiomassEaterComponent> ent, ref ST14EatCorpseActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!CanEat(ent, args.Target, true))
            return;

        StartBite(ent, args.Target);
    }

    private bool CanEat(Entity<ST14BiomassEaterComponent> ent, EntityUid target, bool popup)
    {
        if (!HasComp<MobStateComponent>(target) || HasComp<ST14HiveMemberComponent>(target) || HasComp<ST14HiveQueenComponent>(target))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("st14-hive-eat-not-food"), ent, ent);
            return false;
        }

        if (!_mobState.IsDead(target))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("st14-hive-eat-alive"), ent, ent);
            return false;
        }

        if (TryComp<ST14BugLarvaComponent>(ent, out var larva) && larva.Biomass >= larva.BiomassNeeded)
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("st14-hive-eat-full"), ent, ent);
            return false;
        }

        return true;
    }

    private void StartBite(Entity<ST14BiomassEaterComponent> ent, EntityUid target)
    {
        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.BiteTime, new ST14EatCorpseDoAfterEvent(), ent, target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            DistanceThreshold = 1.5f,
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnEatDoAfter(Entity<ST14BiomassEaterComponent> ent, ref ST14EatCorpseDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || !CanEat(ent, target, false))
            return;

        args.Handled = true;

        var biomass = EnsureComp<ST14BiomassComponent>(target);
        biomass.Remaining--;

        if (ent.Comp.BiteSound != null)
            _audio.PlayPvs(ent.Comp.BiteSound, ent);

        if (TryComp<ST14BugLarvaComponent>(ent, out var larva))
        {
            larva.Biomass++;
            _popup.PopupEntity(Loc.GetString("st14-hive-eat-larva",
                    ("biomass", larva.Biomass),
                    ("needed", larva.BiomassNeeded)),
                ent,
                ent);
            _hive.UpdateUi(ent.Owner);

            // Full: open the menu right away so the larva can pick its caste
            if (larva.Biomass >= larva.BiomassNeeded)
            {
                _popup.PopupEntity(Loc.GetString("st14-hive-eat-full"), ent, ent, PopupType.Medium);
                _ui.TryOpenUi(ent.Owner, ST14HiveEvolutionUiKey.Key, ent.Owner);
            }
        }

        if (biomass.Remaining <= 0)
        {
            ConsumeCorpse(target);
            return;
        }

        // One bite per unit: keep chewing until the larva is full or the bug is interrupted
        args.Repeat = CanEat(ent, target, false);
    }

    /// <summary>
    /// The corpse is eaten to the bone: its gear falls to the ground and the body is gone
    /// </summary>
    private void ConsumeCorpse(EntityUid corpse)
    {
        if (_inventory.TryGetContainerSlotEnumerator(corpse, out var slots))
        {
            while (slots.MoveNext(out var slot))
            {
                _inventory.TryUnequip(corpse, slot.ID, true, true);
            }
        }

        foreach (var held in _hands.EnumerateHeld(corpse).ToList())
        {
            _hands.TryDrop(corpse, held, checkActionBlocker: false);
        }

        _popup.PopupCoordinates(Loc.GetString("st14-hive-corpse-eaten"), Transform(corpse).Coordinates, PopupType.Medium);
        QueueDel(corpse);
    }
}
