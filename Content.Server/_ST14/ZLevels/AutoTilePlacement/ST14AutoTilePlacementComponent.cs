/*
 * Based on CrystallEdge z-levels (MIT License), see LICENSE.TXT in the ZLevels folder.
 */

using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._ST14.ZLevels.AutoTilePlacement;

/// <summary>
/// Automatically place tile under spawned entity, if this entity was spawned from PlacementManager
/// </summary>
[RegisterComponent, Access(typeof(ST14AutoTilePlacementComponent))]
public sealed partial class ST14AutoTilePlacementComponent : Component
{
    [DataField]
    public ProtoId<ContentTileDefinition> Tile = "Plating";
}
