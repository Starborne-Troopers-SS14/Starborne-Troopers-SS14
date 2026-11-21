/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._ST14.ZLevels.Roof;

namespace Content.Server._ST14.ZLevels.Roof;

public sealed partial class ST14ZLevelsRoofSystem : ST14SharedZLevelsRoofSystem
{
    private readonly HashSet<Vector2i> _roofMap = new();

    public override void Initialize()
    {
        base.Initialize();

    }
}
