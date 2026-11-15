/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;

namespace Content.Shared._ST14.ZLevels.Fortifications;

/// <summary>
/// Switches the entrenching tool between trenches into the level below and cable trenches. The digging itself is
/// done on the server by ST14TrenchSystem (z-levels) and the energy fence ST14TrenchSystem (cables).
/// </summary>
public sealed partial class ST14TrenchDiggerSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14TrenchDiggerComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ST14TrenchDiggerComponent, ExaminedEvent>(OnExamined);
    }

    private void OnUseInHand(Entity<ST14TrenchDiggerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ent.Comp.CableMode = !ent.Comp.CableMode;
        Dirty(ent);
        _popup.PopupClient(Loc.GetString(ent.Comp.CableMode ? "st14-trench-mode-cable" : "st14-trench-mode-trench"), args.User, args.User);
    }

    private void OnExamined(Entity<ST14TrenchDiggerComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString(ent.Comp.CableMode ? "st14-trench-mode-cable-examine" : "st14-trench-mode-trench-examine"));
    }
}
