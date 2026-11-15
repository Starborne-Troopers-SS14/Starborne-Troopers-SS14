/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._ST14.ZLevels.Ghost;
using Content.Shared.Actions;
using Robust.Shared.Analyzers;

namespace Content.Server._ST14.ZLevels.Ghost;

public sealed partial class ST14ZLevelGhostMoverSystem : ST14SharedZLevelGhostMoverSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<ST14ZLevelGhostMoverComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ZLevelUpActionEntity, ent.Comp.UpActionProto);
        _actions.AddAction(ent, ref ent.Comp.ZLevelDownActionEntity, ent.Comp.DownActionProto);
    }

    [SubscribeLocalEvent]
    private void OnRemove(Entity<ST14ZLevelGhostMoverComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Comp.ZLevelUpActionEntity);
        _actions.RemoveAction(ent.Comp.ZLevelDownActionEntity);
    }
}
