/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._ST14.ZLevels.Fortifications;

/// <summary>
/// The entrenching tool's jobs: a trench segment, a dug-out, steps, cable trenches. Using it in hand opens the build
/// window on the client (Content.Client/_ST14/ZLevels/Fortifications/ST14TrenchMenuSystem.cs), where the job is picked
/// and the segment placed and turned. The digging itself is done on the server by ST14TrenchSystem (z-levels) and the
/// energy fence ST14TrenchSystem (cables).
/// </summary>
public sealed partial class ST14TrenchDiggerSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

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
        if (_net.IsClient && _timing.IsFirstTimePredicted)
            RaiseLocalEvent(new ST14OpenTrenchMenuEvent(ent));
    }

    private void OnExamined(Entity<ST14TrenchDiggerComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("st14-trench-mode-examine", ("mode", ModeName(ent.Comp.Mode))));
    }

    public string ModeName(ST14DigMode mode)
    {
        return Loc.GetString($"st14-trench-mode-name-{mode.ToString().ToLowerInvariant()}");
    }
}
