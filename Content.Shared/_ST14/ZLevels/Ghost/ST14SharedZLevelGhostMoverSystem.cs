/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */


using Content.Shared._ST14.ZLevels.Core.EntitySystems;
using Robust.Shared.Analyzers;

namespace Content.Shared._ST14.ZLevels.Ghost;

public abstract partial class ST14SharedZLevelGhostMoverSystem : EntitySystem
{
    [Dependency] private ST14SharedZLevelsSystem _zLevel = null!;

    [SubscribeLocalEvent]
    private void OnZLevelDown(Entity<ST14ZLevelGhostMoverComponent> ent, ref ST14ZLevelActionDown args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveDown(ent);
    }

    [SubscribeLocalEvent]
    private void OnZLevelUp(Entity<ST14ZLevelGhostMoverComponent> ent, ref ST14ZLevelActionUp args)
    {
        if (args.Handled)
            return;

        args.Handled = _zLevel.TryMoveUp(ent);
    }
}
