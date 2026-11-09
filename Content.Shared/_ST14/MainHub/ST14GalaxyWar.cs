using System.Linq;
using System.Numerics;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// Honeycomb over a sphere: the dual of a subdivided icosahedron. Every cell is a hexagon, except twelve pentagons
/// </summary>
public sealed class ST14HexGrid
{
    public readonly Vector3[] Centers;
    public readonly Vector3[][] Corners;
    public readonly int[][] Neighbours;

    /// <summary>
    /// The neighbours of each cell by the edge they lie behind, as a pointy-top hex map sees them: 0 is east, then
    /// counter-clockwise. The twelve pentagons have one edge less
    /// </summary>
    public readonly int[][] Edges;

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
        Edges = new int[points.Count][];
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
            Edges[i] = SortEdges(i, points);
        }
    }

    public int Count => Centers.Length;

    private int[] SortEdges(int cell, List<Vector3> points)
    {
        // east and north of the cell on the globe, near a pole any stable frame will do
        var normal = points[cell];
        var east = Vector3.Cross(Vector3.UnitY, normal);
        east = east.LengthSquared() > 0.0001f ? Vector3.Normalize(east) : Vector3.UnitX;
        var north = Vector3.Cross(normal, east);

        float Angle(int other)
        {
            var angle = MathF.Atan2(Vector3.Dot(points[other] - normal, north), Vector3.Dot(points[other] - normal, east));
            return angle < 0 ? angle + MathF.Tau : angle;
        }

        // the first edge is the one closest to east whichever way the cell lies in the honeycomb
        var sorted = Neighbours[cell].OrderBy(Angle).ToArray();
        var first = 0;
        var best = float.MaxValue;
        for (var i = 0; i < sorted.Length; i++)
        {
            var angle = Angle(sorted[i]);
            var off = MathF.Min(angle, MathF.Tau - angle);
            if (off >= best)
                continue;

            best = off;
            first = i;
        }

        return sorted.Skip(first).Concat(sorted.Take(first)).ToArray();
    }

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

    /// <summary>
    /// The region of seven cells this cell belongs to, see <see cref="ST14PlanetWar.RegionCenters"/>
    /// </summary>
    public int Region;

    /// <summary>
    /// Has been fought in: it has a saved map, which loads every round and can be walked into
    /// </summary>
    public bool Initialized;

    public bool Hive;
    public bool Base;

    /// <summary>
    /// The command whose turn it is can pick this cell as the sector of the next battle
    /// </summary>
    public bool Front;

    /// <summary>
    /// Dead counted in the rounds fought in this sector
    /// </summary>
    public int BugsKilled;
    public int TroopersKilled;
}

/// <summary>
/// The war on one planet: the hex grid and who holds each cell. The first state comes from the planet seed,
/// after that the server keeps it and sends it to the clients
/// </summary>
public sealed class ST14PlanetWar
{
    public const int TerrainKinds = 6;
    private const int MinRegionSize = 3;

    public readonly ST14HexGrid Grid;
    public readonly ST14HexCell[] Cells;

    /// <summary>
    /// The central cell of every region: a carrier hangs over it and the conquest of the region starts there
    /// </summary>
    public readonly int[] RegionCenters;

    /// <summary>
    /// The side whose command picks the sector of the next battle. The initiative changes after every round
    /// </summary>
    public ST14GalaxyFaction Turn = ST14GalaxyFaction.Federation;

    /// <summary>
    /// The region the war is fought in, -1 between regions. The first battle locks it, and both commands fight only
    /// there until the Federation holds all of it
    /// </summary>
    public int ActiveRegion = -1;

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

        // the best placed cells go to the Federation until the liberation of the planet is reached, a band of contested ones follows.
        // A planet the Federation has not set foot on yet only gets its landing zones
        var order = Enumerable.Range(0, Grid.Count).OrderByDescending(i => score[i]).ToArray();
        var held = (int) MathF.Round(Grid.Count * Math.Clamp(planet.Liberation, 0, 100) / 100f * 0.75f);

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
            else
            {
                cell.Faction = ST14GalaxyFaction.Arachnid;
                cell.Hive = hash < 0.09f;
            }

            Cells[i] = cell;
        }

        RegionCenters = planet.Regions.Count == Cells.Length ? UseRegions(planet.Regions) : BuildRegions();
        RefreshFront();
    }

    /// <summary>
    /// Takes the regions from the prototype. The centre of a region is its cell with the most of the region around it
    /// </summary>
    private int[] UseRegions(List<int> regions)
    {
        var centers = new int[regions.Max() + 1];
        for (var r = 0; r < centers.Length; r++)
        {
            var best = -1;
            for (var i = 0; i < regions.Count; i++)
            {
                if (regions[i] != r)
                    continue;

                Cells[i].Region = r;
                var around = Grid.Neighbours[i].Count(n => regions[n] == r);
                if (around <= best)
                    continue;

                best = around;
                centers[r] = i;
            }
        }

        return centers;
    }

    /// <summary>
    /// Splits the honeycomb into regions: a centre with its neighbours. Twelve pentagons keep a sphere from dividing
    /// evenly, so some cells are left between the regions: a group of three or more makes a smaller region of its own,
    /// a lone cell or two join the smallest region next to them. Deterministic, the server and the clients build the
    /// same regions from the same grid
    /// </summary>
    private int[] BuildRegions()
    {
        var region = new int[Cells.Length];
        Array.Fill(region, -1);
        var centers = new List<int>();

        while (true)
        {
            // the free centre that touches the taken ground most: the regions grow tightly side by side
            var best = -1;
            var bestScore = -1;
            for (var i = 0; i < Cells.Length; i++)
            {
                if (region[i] >= 0 || Grid.Neighbours[i].Any(n => region[n] >= 0))
                    continue;

                var score = Grid.Neighbours[i].Sum(n => Grid.Neighbours[n].Count(m => region[m] >= 0));
                if (score <= bestScore)
                    continue;

                bestScore = score;
                best = i;
            }

            if (best < 0)
                break;

            region[best] = centers.Count;
            foreach (var n in Grid.Neighbours[best])
            {
                region[n] = centers.Count;
            }

            centers.Add(best);
        }

        // what is left between the regions, group by group
        var seen = new bool[region.Length];
        for (var i = 0; i < region.Length; i++)
        {
            if (region[i] >= 0 || seen[i])
                continue;

            var group = new List<int> { i };
            seen[i] = true;
            for (var g = 0; g < group.Count; g++)
            {
                foreach (var n in Grid.Neighbours[group[g]])
                {
                    if (region[n] >= 0 || seen[n])
                        continue;

                    seen[n] = true;
                    group.Add(n);
                }
            }

            if (group.Count < MinRegionSize)
                continue;

            // the cell with the most of the group around it is the centre
            var center = group.OrderByDescending(c => Grid.Neighbours[c].Count(group.Contains)).ThenBy(c => c).First();
            foreach (var cell in group)
            {
                region[cell] = centers.Count;
            }

            centers.Add(center);
        }

        var sizes = new int[centers.Count];
        foreach (var r in region)
        {
            if (r >= 0)
                sizes[r]++;
        }

        var left = true;
        while (left)
        {
            left = false;
            for (var i = 0; i < region.Length; i++)
            {
                if (region[i] >= 0)
                    continue;

                var taken = Grid.Neighbours[i].Where(n => region[n] >= 0).Select(n => region[n]).ToArray();
                if (taken.Length == 0)
                {
                    left = true;
                    continue;
                }

                region[i] = taken.OrderBy(r => sizes[r]).ThenBy(r => r).First();
                sizes[region[i]]++;
            }
        }

        for (var i = 0; i < Cells.Length; i++)
        {
            Cells[i].Region = region[i];
        }

        return centers.ToArray();
    }

    public static ST14GalaxyFaction Enemy(ST14GalaxyFaction side)
    {
        return side == ST14GalaxyFaction.Arachnid ? ST14GalaxyFaction.Federation : ST14GalaxyFaction.Arachnid;
    }

    /// <summary>
    /// Marks the cells the command of the side whose turn it is can pick
    /// </summary>
    public void RefreshFront()
    {
        var options = TargetOptions(Turn);
        for (var i = 0; i < Cells.Length; i++)
        {
            Cells[i].Front = options.Contains(i);
        }
    }

    /// <summary>
    /// The cells a side can start the battle of the next round in.
    /// The Federation takes a region from its centre outwards: the centre first, then what lies next to its ground there.
    /// The bugs strike back at the Federation's ground. With no such ground left they only choose which of the sectors
    /// already fought over to hold, and the Federation lands there again
    /// </summary>
    public HashSet<int> TargetOptions(ST14GalaxyFaction side)
    {
        var options = new HashSet<int>();
        var locked = ActiveRegion >= 0 && ActiveRegion < RegionCenters.Length;
        if (side == ST14GalaxyFaction.Federation)
        {
            for (var r = 0; r < RegionCenters.Length; r++)
            {
                if (locked && r != ActiveRegion)
                    continue;

                var held = false;
                for (var i = 0; i < Cells.Length; i++)
                {
                    if (Cells[i].Region != r || Cells[i].Faction != ST14GalaxyFaction.Federation)
                        continue;

                    held = true;
                    foreach (var n in Grid.Neighbours[i])
                    {
                        if (Cells[n].Region == r && Cells[n].Faction != ST14GalaxyFaction.Federation)
                            options.Add(n);
                    }
                }

                if (!held)
                    options.Add(RegionCenters[r]);
            }

            return options;
        }

        bool InWar(int i) => !locked || Cells[i].Region == ActiveRegion;

        for (var i = 0; i < Cells.Length; i++)
        {
            if (InWar(i) && Cells[i].Faction == ST14GalaxyFaction.Federation)
                options.Add(i);
        }

        if (options.Count > 0)
            return options;

        for (var i = 0; i < Cells.Length; i++)
        {
            if (InWar(i) && Cells[i].Initialized)
                options.Add(i);
        }

        if (options.Count == 0)
        {
            if (locked)
                options.Add(RegionCenters[ActiveRegion]);
            else
                options.UnionWith(RegionCenters);
        }

        return options;
    }

    /// <summary>
    /// Neighbours of the target the attacking side can gather in: cells it holds there. The attacker is whoever does not hold the target
    /// </summary>
    public IEnumerable<int> StagingOptions(int target)
    {
        var attacker = Enemy(Cells[target].Faction);
        return Grid.Edges[target].Where(n => Cells[n].Faction == attacker);
    }

    /// <summary>
    /// The sector a side starts the battle in: the target if it holds it, otherwise the staging sector,
    /// and with neither the target itself, as a landing from the carrier
    /// </summary>
    public int StartOf(ST14GalaxyFaction side, int target, int staging)
    {
        return Cells[target].Faction != side && staging >= 0 ? staging : target;
    }

    /// <summary>
    /// Result of a round: one sector changes hands at most and the initiative goes to the other side. A null winner
    /// is a battle that ran out of time, the attack has failed. A staging sector of the bugs falls to troopers who
    /// wipe the bugs out even though it was the bugs who were attacking
    /// </summary>
    /// <remarks>
    /// A sector with resource points goes by them instead: <paramref name="points"/> holds such sectors of the battle,
    /// each with the side that holds all of its points, or null when no side does and the sector stays as it is.
    /// Returns true when the Federation took the whole region of the war, which frees the commands to pick another one
    /// </remarks>
    public bool ApplyResult(int target, int staging, ST14GalaxyFaction? winner, IReadOnlyDictionary<int, ST14GalaxyFaction?>? points = null)
    {
        LockRegion(target);

        var holder = Cells[target].Faction;
        var byPoints = points ?? new Dictionary<int, ST14GalaxyFaction?>();
        if (winner is { } side)
        {
            if (side != holder && !byPoints.ContainsKey(target))
                SetHolder(target, side);
            else if (side == holder && side == ST14GalaxyFaction.Federation && staging >= 0 && !byPoints.ContainsKey(staging))
                SetHolder(staging, side);
        }

        foreach (var (cell, pointsHolder) in byPoints)
        {
            if (pointsHolder is { } taker && cell >= 0 && cell < Cells.Length && Cells[cell].Faction != taker)
                SetHolder(cell, taker);
        }

        var taken = ActiveRegion >= 0 && Cells.All(c => c.Region != ActiveRegion || c.Faction == ST14GalaxyFaction.Federation);
        if (taken)
            ActiveRegion = -1;

        Turn = Enemy(Turn);
        RefreshFront();
        return taken;
    }

    /// <summary>
    /// A battle started in the region of the cell: when no region is fought for yet, the war is now bound to this one
    /// </summary>
    public void LockRegion(int cell)
    {
        if (ActiveRegion < 0 && cell >= 0 && cell < Cells.Length)
            ActiveRegion = Cells[cell].Region;
    }

    private void SetHolder(int index, ST14GalaxyFaction side)
    {
        var cell = Cells[index];
        cell.Faction = side;
        cell.Liberation = side == ST14GalaxyFaction.Federation ? 100 : 0;
        if (side == ST14GalaxyFaction.Federation)
            cell.Hive = false;
        else
            cell.Base = false;
    }

    public ST14HexCellState[] ExportState()
    {
        return Cells.Select(c => new ST14HexCellState(c.Faction, c.Liberation, c.Hive, c.Base, c.BugsKilled, c.TroopersKilled, c.Initialized)).ToArray();
    }

    /// <summary>
    /// The dead of all the cells of the region of a cell: a round may be fought over several of them
    /// </summary>
    public (int Bugs, int Troopers) RegionKills(int index)
    {
        var region = Cells[index].Region;
        var cells = Cells.Where(c => c.Region == region).ToArray();
        return (cells.Sum(c => c.BugsKilled), cells.Sum(c => c.TroopersKilled));
    }

    /// <summary>
    /// Takes the state of the cells from the server or from a save. A state made for another grid is ignored
    /// </summary>
    public bool ImportState(IReadOnlyList<ST14HexCellState> states, ST14GalaxyFaction turn, int activeRegion = -1)
    {
        ActiveRegion = activeRegion >= 0 && activeRegion < RegionCenters.Length ? activeRegion : -1;
        if (states.Count != Cells.Length)
            return false;

        for (var i = 0; i < Cells.Length; i++)
        {
            var state = states[i];
            Cells[i].Faction = state.Faction;
            Cells[i].Liberation = state.Liberation;
            Cells[i].Hive = state.Hive;
            Cells[i].Base = state.Base;
            Cells[i].BugsKilled = state.BugsKilled;
            Cells[i].TroopersKilled = state.TroopersKilled;
            Cells[i].Initialized = state.Initialized;
        }

        Turn = turn;
        RefreshFront();
        return true;
    }

    /// <summary>
    /// Liberation of the whole planet, in percent
    /// </summary>
    public float Liberation => Cells.Sum(c => c.Liberation) / Cells.Length;

    /// <summary>
    /// First cell the command can pick, null when there is none
    /// </summary>
    public int? FirstLanding
    {
        get
        {
            var index = Array.FindIndex(Cells, c => c.Front);
            return index >= 0 ? index : null;
        }
    }

    public int BugsKilled => Cells.Sum(c => c.BugsKilled);

    public int TroopersKilled => Cells.Sum(c => c.TroopersKilled);

    public int CountOf(ST14GalaxyFaction faction)
    {
        return Cells.Count(c => c.Faction == faction);
    }
}

/// <summary>
/// Smooth 3D value noise, sampled on the unit sphere so planet surfaces have no seam
/// </summary>
public static class ST14Noise
{
    private static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            var h = (uint) (seed * 1013904223 + x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    public static float Hash(int value, int seed)
    {
        return Hash(value, value * 7 + 3, seed * 13 + 1, seed);
    }

    private static float Smooth(float t)
    {
        return t * t * (3 - 2 * t);
    }

    public static float Value(Vector3 p, int seed)
    {
        var x0 = (int) MathF.Floor(p.X);
        var y0 = (int) MathF.Floor(p.Y);
        var z0 = (int) MathF.Floor(p.Z);
        var fx = Smooth(p.X - x0);
        var fy = Smooth(p.Y - y0);
        var fz = Smooth(p.Z - z0);

        var c000 = Hash(x0, y0, z0, seed);
        var c100 = Hash(x0 + 1, y0, z0, seed);
        var c010 = Hash(x0, y0 + 1, z0, seed);
        var c110 = Hash(x0 + 1, y0 + 1, z0, seed);
        var c001 = Hash(x0, y0, z0 + 1, seed);
        var c101 = Hash(x0 + 1, y0, z0 + 1, seed);
        var c011 = Hash(x0, y0 + 1, z0 + 1, seed);
        var c111 = Hash(x0 + 1, y0 + 1, z0 + 1, seed);

        var x00 = c000 + (c100 - c000) * fx;
        var x10 = c010 + (c110 - c010) * fx;
        var x01 = c001 + (c101 - c001) * fx;
        var x11 = c011 + (c111 - c011) * fx;
        var y0v = x00 + (x10 - x00) * fy;
        var y1v = x01 + (x11 - x01) * fy;
        return y0v + (y1v - y0v) * fz;
    }

    public static float Fbm(Vector3 p, int octaves, int seed)
    {
        var sum = 0f;
        var amplitude = 0.5f;
        var total = 0f;
        for (var i = 0; i < octaves; i++)
        {
            sum += Value(p, seed + i * 31) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            p *= 2.03f;
        }

        return sum / total;
    }
}

public static class ST14Sphere
{
    /// <summary>
    /// Point of the unit sphere, y is the polar axis
    /// </summary>
    public static Vector3 Point(float lon, float lat)
    {
        var c = MathF.Cos(lat);
        return new Vector3(c * MathF.Cos(lon), MathF.Sin(lat), c * MathF.Sin(lon));
    }
}
