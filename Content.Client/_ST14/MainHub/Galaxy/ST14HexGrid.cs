using System.Linq;
using System.Numerics;
using Content.Shared._ST14.MainHub;

namespace Content.Client._ST14.MainHub.Galaxy;

/// <summary>
/// Honeycomb over a sphere: the dual of a subdivided icosahedron. Every cell is a hexagon, except twelve pentagons
/// </summary>
public sealed class ST14HexGrid
{
    public readonly Vector3[] Centers;
    public readonly Vector3[][] Corners;
    public readonly int[][] Neighbours;

    private static readonly float T = (1 + MathF.Sqrt(5)) / 2;

    private static readonly Vector3[] IcoVertices =
    {
        new(-1, T, 0), new(1, T, 0), new(-1, -T, 0), new(1, -T, 0),
        new(0, -1, T), new(0, 1, T), new(0, -1, -T), new(0, 1, -T),
        new(T, 0, -1), new(T, 0, 1), new(-T, 0, -1), new(-T, 0, 1),
    };

    private static readonly int[] IcoFaces =
    {
        0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
        1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
        3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
        4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
    };

    public ST14HexGrid(int frequency)
    {
        frequency = Math.Max(1, frequency);
        var points = new List<Vector3>();
        var lookup = new Dictionary<(int, int, int), int>();
        var triangles = new List<(int A, int B, int C)>();

        int Point(Vector3 p)
        {
            p = Vector3.Normalize(p);
            var key = ((int) MathF.Round(p.X * 1000), (int) MathF.Round(p.Y * 1000), (int) MathF.Round(p.Z * 1000));
            if (lookup.TryGetValue(key, out var index))
                return index;

            lookup[key] = points.Count;
            points.Add(p);
            return points.Count - 1;
        }

        for (var f = 0; f < IcoFaces.Length; f += 3)
        {
            var a = IcoVertices[IcoFaces[f]];
            var b = IcoVertices[IcoFaces[f + 1]];
            var c = IcoVertices[IcoFaces[f + 2]];
            var grid = new int[frequency + 1, frequency + 1];
            for (var i = 0; i <= frequency; i++)
            {
                for (var j = 0; j <= frequency - i; j++)
                {
                    grid[i, j] = Point(a + (b - a) * i / frequency + (c - a) * j / frequency);
                }
            }

            for (var i = 0; i < frequency; i++)
            {
                for (var j = 0; j < frequency - i; j++)
                {
                    triangles.Add((grid[i, j], grid[i + 1, j], grid[i, j + 1]));
                    if (i + j < frequency - 1)
                        triangles.Add((grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]));
                }
            }
        }

        var faces = new List<Vector3>[points.Count];
        var neighbours = new HashSet<int>[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            faces[i] = new List<Vector3>();
            neighbours[i] = new HashSet<int>();
        }

        foreach (var (a, b, c) in triangles)
        {
            var centroid = Vector3.Normalize(points[a] + points[b] + points[c]);
            faces[a].Add(centroid);
            faces[b].Add(centroid);
            faces[c].Add(centroid);
            neighbours[a].Add(b);
            neighbours[a].Add(c);
            neighbours[b].Add(a);
            neighbours[b].Add(c);
            neighbours[c].Add(a);
            neighbours[c].Add(b);
        }

        Centers = points.ToArray();
        Corners = new Vector3[points.Count][];
        Neighbours = new int[points.Count][];
        for (var i = 0; i < points.Count; i++)
        {
            // corners go around the cell centre in one direction
            var normal = points[i];
            var tangent = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX, normal));
            var bitangent = Vector3.Cross(normal, tangent);
            Corners[i] = faces[i]
                .OrderBy(p => MathF.Atan2(Vector3.Dot(p - normal, bitangent), Vector3.Dot(p - normal, tangent)))
                .ToArray();
            Neighbours[i] = neighbours[i].ToArray();
        }
    }

    public int Count => Centers.Length;

    /// <summary>
    /// Cell whose centre is closest to the point of the unit sphere
    /// </summary>
    public int Nearest(Vector3 point)
    {
        var best = 0;
        var bestDot = float.MinValue;
        for (var i = 0; i < Centers.Length; i++)
        {
            var dot = Vector3.Dot(Centers[i], point);
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            best = i;
        }

        return best;
    }
}

/// <summary>
/// One hex sector of a war planet
/// </summary>
public sealed class ST14HexCell
{
    public ST14GalaxyFaction Faction;

    /// <summary>
    /// Share held by the Federation, in percent
    /// </summary>
    public float Liberation;

    public string Code = string.Empty;
    public int Terrain;
    public bool Hive;
    public bool Base;

    /// <summary>
    /// Not held by the Federation but next to its ground: troopers can land here
    /// </summary>
    public bool Front;
}

/// <summary>
/// The war on one planet: the hex grid and who holds each cell. Made from the planet seed, so every client sees the same front
/// </summary>
public sealed class ST14PlanetWar
{
    public const int TerrainKinds = 6;

    public readonly ST14HexGrid Grid;
    public readonly ST14HexCell[] Cells;

    public ST14PlanetWar(ST14GalaxyPlanetPrototype planet, string codePrefix)
    {
        Grid = new ST14HexGrid(planet.HexFrequency);
        Cells = new ST14HexCell[Grid.Count];

        var seed = planet.Seed * 131 + 5;
        // the landing zone of the Federation, the front spreads from it
        var landing = Vector3.Normalize(new Vector3(
            ST14Noise.Hash(seed, 11) - 0.5f,
            0.35f + ST14Noise.Hash(seed, 12) * 0.3f,
            ST14Noise.Hash(seed, 13) - 0.5f));

        var score = new float[Grid.Count];
        for (var i = 0; i < Grid.Count; i++)
        {
            var c = Grid.Centers[i];
            score[i] = Vector3.Dot(c, landing) + (ST14Noise.Fbm(c * 2.5f + new Vector3(seed % 17), 3, seed) - 0.5f) * 0.7f;
        }

        // the best placed cells go to the Federation until the liberation of the planet is reached, a band of contested ones follows
        var order = Enumerable.Range(0, Grid.Count).OrderByDescending(i => score[i]).ToArray();
        var held = (int) MathF.Round(Grid.Count * Math.Clamp(planet.Liberation, 0, 100) / 100f * 0.75f);
        var contested = held + (int) MathF.Round(Grid.Count * 0.1f);

        for (var rank = 0; rank < order.Length; rank++)
        {
            var i = order[rank];
            var hash = ST14Noise.Hash(i, seed);
            var cell = new ST14HexCell
            {
                Code = $"{codePrefix}-{i + 1:000}",
                Terrain = (int) (ST14Noise.Hash(i, seed + 1) * TerrainKinds) % TerrainKinds,
            };

            if (rank < held)
            {
                cell.Faction = ST14GalaxyFaction.Federation;
                cell.Liberation = 100;
                cell.Base = hash < 0.12f;
            }
            else if (rank < contested)
            {
                cell.Faction = ST14GalaxyFaction.Contested;
                cell.Liberation = 15 + MathF.Round(hash * 70);
            }
            else
            {
                cell.Faction = ST14GalaxyFaction.Arachnid;
                cell.Hive = hash < 0.09f;
            }

            Cells[i] = cell;
        }

        for (var i = 0; i < Cells.Length; i++)
        {
            var cell = Cells[i];
            cell.Front = cell.Faction == ST14GalaxyFaction.Contested ||
                         cell.Faction == ST14GalaxyFaction.Arachnid &&
                         Grid.Neighbours[i].Any(n => Cells[n].Faction == ST14GalaxyFaction.Federation);
        }
    }

    /// <summary>
    /// Liberation of the whole planet, in percent
    /// </summary>
    public float Liberation => Cells.Sum(c => c.Liberation) / Cells.Length;

    public int CountOf(ST14GalaxyFaction faction)
    {
        return Cells.Count(c => c.Faction == faction);
    }
}
