using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Tiles;

/// <summary>
/// Makes a tile draw one big texture across several tiles instead of random variants.
/// The tile's variants must be the big texture cut into Width x Height pieces, row by row from the top left,
/// and every tile then takes the piece that matches its position on the grid
/// </summary>
[Prototype("st14MultiTile")]
public sealed partial class ST14MultiTilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<ContentTileDefinition> Tile;

    /// <summary>
    /// Size of the big texture in tiles
    /// </summary>
    [DataField]
    public int Width = 4;

    [DataField]
    public int Height = 4;
}
