/*
 * Starborne Troopers z-level layers. Part of the z-levels based on CrystallEdge (MIT License),
 * see LICENSE.TXT in the ZLevels folder.
 */

using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.ZLevels.Layers;

/// <summary>
/// The levels a map gets above and below it. The main map is depth 0; every entry here becomes a map of its own,
/// linked into one z-network with it. Used by the sectors of the galaxy war (see ST14PlanetSector) and by the
/// znetwork-layers admin command.
/// </summary>
[Prototype("st14ZLayerSet")]
public sealed partial class ST14ZLayerSetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The levels around depth 0. Depth 0 itself is the main map and is never listed
    /// </summary>
    [DataField(required: true)]
    public List<ST14ZLayer> Layers = new();

    /// <summary>
    /// Component that marks the edge of the main map (the sandstorm around a sector). A filled layer gets its ground
    /// under the open part of the main map plus the innermost ring of that edge, and an <see cref="ST14ZLayer.Edge"/>
    /// on each ring tile. Without it the whole footprint of the main map is copied and no edge is placed
    /// </summary>
    [DataField]
    public string? BoundaryComponent;
}

[DataDefinition]
public sealed partial class ST14ZLayer
{
    [DataField(required: true)]
    public int Depth;

    /// <summary>
    /// What the level is for, shown to admins and in the map name
    /// </summary>
    [DataField]
    public LocId? Name;

    /// <summary>
    /// Ground tile laid under the whole footprint of the main map. Null leaves the level empty (open air)
    /// </summary>
    [DataField]
    public ProtoId<ContentTileDefinition>? Fill;

    /// <summary>
    /// Entity put on every edge tile of a filled level, so nobody walks off the ground
    /// </summary>
    [DataField]
    public EntProtoId? Edge;
}
