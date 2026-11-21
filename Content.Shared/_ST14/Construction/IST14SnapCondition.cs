using Robust.Shared.Map;

namespace Content.Shared._ST14.Construction;

/// <summary>
/// A condition of an engineer buildable that also says where exactly it goes: the blueprint is put there instead of
/// on the tile under the cursor (a bunker upgrade goes in the middle of its bunker). Both the ghost and the server use
/// it (<c>SharedST14MultitoolSystem.GetPlacementError</c>).
/// </summary>
public interface IST14SnapCondition
{
    bool TrySnap(EntityCoordinates location, Direction direction, out EntityCoordinates spot);
}
