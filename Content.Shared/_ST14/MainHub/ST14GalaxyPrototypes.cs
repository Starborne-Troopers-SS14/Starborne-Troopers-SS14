using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// Side holding a sector, a planet or a part of a planet on the galaxy map
/// </summary>
public enum ST14GalaxyFaction : byte
{
    Federation,
    Arachnid,
    Contested,
    Unexplored,
}

/// <summary>
/// Sector of the Milky Way on the galaxy map: a piece of one ring between two angles. Ring 0 is the disc around Earth in the centre
/// </summary>
[Prototype("st14GalaxySector")]
public sealed partial class ST14GalaxySectorPrototype : IPrototype
{
    /// <summary>
    /// Outer radius of every ring, the galaxy has a radius of 1
    /// </summary>
    public static readonly float[] RingRadius = { 0.11f, 0.26f, 0.42f, 0.58f, 0.74f, 0.9f };

    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public int Ring;

    /// <summary>
    /// Start and end of the sector in degrees, clockwise from the right side of the map. The end may go past 360
    /// </summary>
    [DataField]
    public float Start;

    [DataField]
    public float End = 360;

    [DataField]
    public ST14GalaxyFaction Faction = ST14GalaxyFaction.Unexplored;
}

/// <summary>
/// Planet on the galaxy map. The surface is generated from the seed and the colours, war planets are split into hex sectors
/// </summary>
[Prototype("st14GalaxyPlanet")]
public sealed partial class ST14GalaxyPlanetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public LocId? Description;

    [DataField(required: true)]
    public ProtoId<ST14GalaxySectorPrototype> Sector;

    /// <summary>
    /// Distance from the galactic core, the galaxy has a radius of 1
    /// </summary>
    [DataField]
    public float Radius;

    /// <summary>
    /// Direction from the core in degrees, clockwise from the right side of the map
    /// </summary>
    [DataField]
    public float Angle;

    [DataField]
    public ST14GalaxyFaction Faction = ST14GalaxyFaction.Unexplored;

    /// <summary>
    /// Share of the planet held by the Federation, in percent
    /// </summary>
    [DataField]
    public float Liberation;

    /// <summary>
    /// Seat of the Federation, drawn with a bigger marker
    /// </summary>
    [DataField]
    public bool Capital;

    /// <summary>
    /// The planet can be zoomed into: its surface is split into hex sectors that are fought over, landing leads to the lobby
    /// </summary>
    [DataField]
    public bool War;

    /// <summary>
    /// Subdivision of the hex grid, a grid of frequency n has 10 n^2 + 2 cells
    /// </summary>
    [DataField]
    public int HexFrequency = 5;

    /// <summary>
    /// Hex sectors where troopers can land when the Federation holds nothing on the planet yet
    /// </summary>
    [DataField]
    public int LandingZones = 1;

    [DataField]
    public int Seed;

    /// <summary>
    /// Surface colours from the lowlands to the peaks
    /// </summary>
    [DataField]
    public List<Color> Surface = new() { Color.FromHex("#3B2A20"), Color.FromHex("#8A5A3A"), Color.FromHex("#D9B48A") };

    /// <summary>
    /// Painted surface of the planet, an equirectangular map twice as wide as it is high. Without it the surface is made from the seed
    /// </summary>
    [DataField]
    public ResPath? SurfaceTexture;

    [DataField]
    public Color Atmosphere = Color.FromHex("#7FB8E6");

    /// <summary>
    /// Supply lines to other planets
    /// </summary>
    [DataField]
    public List<ProtoId<ST14GalaxyPlanetPrototype>> Links = new();
}
