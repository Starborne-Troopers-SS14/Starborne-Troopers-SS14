using System.Numerics;
using Content.Shared._ST14.Sectors;
using Robust.Client.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// Paints the pictures of the battle map: the ground of a sector like a field map, the hatching of the sectors seen only
/// from afar and the shade at the edges of the view
/// </summary>
public sealed partial class ST14BattleMapClientSystem
{
    /// <summary>
    /// Pixels of the picture for one block of the ground
    /// </summary>
    private const int ArtScale = 3;

    /// <summary>
    /// Blocks from the rock over which the ground darkens
    /// </summary>
    private const int ShadeReach = 5;

    private static readonly Vector3 SandDark = new(124, 98, 66);
    private static readonly Vector3 SandLight = new(162, 132, 92);
    private static readonly Vector3 RockDark = new(38, 31, 27);
    private static readonly Vector3 RockLight = new(66, 54, 44);
    private static readonly Vector3 Rim = new(214, 184, 128);

    private Texture? _hatch;
    private Texture? _vignette;

    /// <summary>
    /// The shape of a sector filled with slanted lines, white to be tinted with the colour of its owner
    /// </summary>
    public Texture Hatch()
    {
        if (_hatch != null)
            return _hatch;

        const int size = 256;
        using var image = new Image<Rgba32>(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var tile = (new Vector2(x, size - 1 - y) + new Vector2(0.5f)) / size * ST14SectorGeometry.Size;
                if (!ST14SectorGeometry.Contains(tile))
                    continue;

                var edge = Math.Clamp(EdgeDistance(tile) / 14f, 0f, 1f);
                var stripe = (x + y) % 12 < 3;
                var alpha = (stripe ? 0.16f : 0.04f) + (1f - edge) * 0.22f;
                image[x, y] = new Rgba32(255, 255, 255, (byte) (alpha * 255));
            }
        }

        _hatch = Texture.LoadFromImage(image, "st14-battle-map-hatch", Smooth);
        return _hatch;
    }

    /// <summary>
    /// Black growing at the corners, laid over the whole view
    /// </summary>
    public Texture Vignette()
    {
        if (_vignette != null)
            return _vignette;

        const int size = 64;
        using var image = new Image<Rgba32>(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var at = (new Vector2(x, y) + new Vector2(0.5f)) / size * 2 - Vector2.One;
                var far = Math.Clamp((at.Length() - 0.55f) / 0.85f, 0f, 1f);
                image[x, y] = new Rgba32(0, 0, 0, (byte) (far * far * 200));
            }
        }

        _vignette = Texture.LoadFromImage(image, "st14-battle-map-vignette", Smooth);
        return _vignette;
    }

    /// <summary>
    /// How far a point of a sector is from its border, in tiles
    /// </summary>
    private static float EdgeDistance(Vector2 tile)
    {
        var local = tile - ST14SectorGeometry.Center;
        var far = 0f;
        for (var i = 0; i < 6; i++)
        {
            far = MathF.Max(far, Vector2.Dot(local, ST14SectorGeometry.EdgeNormal(i)));
        }

        return ST14SectorGeometry.Apothem - far;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint) (x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float) 0xFFFFFF;
        }
    }

    private static float Noise(Vector2 at, int seed)
    {
        var x = (int) MathF.Floor(at.X);
        var y = (int) MathF.Floor(at.Y);
        var fx = at.X - x;
        var fy = at.Y - y;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);

        var bottom = float.Lerp(Hash(x, y, seed), Hash(x + 1, y, seed), fx);
        var top = float.Lerp(Hash(x, y + 1, seed), Hash(x + 1, y + 1, seed), fx);
        return float.Lerp(bottom, top, fy);
    }

    private static float Relief(Vector2 tile, int seed)
    {
        return Noise(tile / 34f, seed) * 0.7f + Noise(tile / 11f, seed + 7) * 0.3f;
    }

    private static Rgba32 Pixel(Vector3 color, float alpha = 1f)
    {
        color = Vector3.Clamp(color, Vector3.Zero, new Vector3(255));
        return new Rgba32((byte) color.X, (byte) color.Y, (byte) color.Z, (byte) (alpha * 255));
    }

    /// <summary>
    /// The ground of a sector as a field map: sand with its hills, the rock hatched and outlined, a grid every ten tiles
    /// </summary>
    private Image<Rgba32> PaintTerrain(int cell, byte[] data)
    {
        const int blocks = ST14BattleMapTerrainEvent.Blocks;
        const int size = blocks * ArtScale;
        const float tilesPerPixel = (float) ST14BattleMapTerrainEvent.BlockTiles / ArtScale;

        bool Solid(int bx, int by)
        {
            if (bx < 0 || by < 0 || bx >= blocks || by >= blocks)
                return true;

            return data[by * blocks + bx] != ST14BattleMapTerrainEvent.Ground;
        }

        // how far each block of ground is from the rock, to darken the ground around it
        var distance = new int[blocks * blocks];
        var queue = new Queue<(int X, int Y)>();
        for (var by = 0; by < blocks; by++)
        {
            for (var bx = 0; bx < blocks; bx++)
            {
                var solid = Solid(bx, by);
                distance[by * blocks + bx] = solid ? 0 : ShadeReach;
                if (solid)
                    queue.Enqueue((bx, by));
            }
        }

        while (queue.TryDequeue(out var at))
        {
            var next = distance[at.Y * blocks + at.X] + 1;
            if (next >= ShadeReach)
                continue;

            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var (nx, ny) = (at.X + dx, at.Y + dy);
                if (nx < 0 || ny < 0 || nx >= blocks || ny >= blocks || distance[ny * blocks + nx] <= next)
                    continue;

                distance[ny * blocks + nx] = next;
                queue.Enqueue((nx, ny));
            }
        }

        var image = new Image<Rgba32>(size, size);
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                var tile = (new Vector2(px, py) + new Vector2(0.5f)) * tilesPerPixel;
                if (!ST14SectorGeometry.Contains(tile))
                    continue;

                var bx = px / ArtScale;
                var by = py / ArtScale;
                var block = data[by * blocks + bx];
                if (block == ST14BattleMapTerrainEvent.Empty)
                    continue;

                var height = Relief(tile, cell);

                // light from the upper left over the hills
                var slope = Relief(tile + new Vector2(-2f, 2f), cell) - Relief(tile - new Vector2(-2f, 2f), cell);
                var light = 1f + slope * 1.6f + (Hash(px, py, cell) - 0.5f) * 0.05f;

                Vector3 color;
                if (block == ST14BattleMapTerrainEvent.Obstacle)
                {
                    // the rock is hatched like on a field map, its border drawn in light brass
                    color = Vector3.Lerp(RockDark, RockLight, height) * light;
                    if ((px + size - py) % 7 < 2)
                        color *= 0.7f;

                    var border = px % ArtScale == 0 && !Solid(bx - 1, by)
                                 || px % ArtScale == ArtScale - 1 && !Solid(bx + 1, by)
                                 || py % ArtScale == 0 && !Solid(bx, by - 1)
                                 || py % ArtScale == ArtScale - 1 && !Solid(bx, by + 1);
                    if (border)
                        color = Vector3.Lerp(color, Rim, 0.55f);
                }
                else
                {
                    color = Vector3.Lerp(SandDark, SandLight, height) * light;

                    // the lines of height every tenth of the hill
                    var band = height * 10f;
                    if (MathF.Abs(band - MathF.Round(band)) < 0.05f)
                        color *= 0.9f;

                    var near = distance[by * blocks + bx];
                    color *= 1f - 0.3f * (1f - near / (float) ShadeReach);

                    // the grid of the map, every ten tiles
                    var grid = tile / 10f;
                    if (MathF.Abs(grid.X - MathF.Round(grid.X)) < tilesPerPixel / 20f || MathF.Abs(grid.Y - MathF.Round(grid.Y)) < tilesPerPixel / 20f)
                        color *= 0.9f;
                }

                // the edge of the sector sinks into the dark
                var edge = Math.Clamp(EdgeDistance(tile) / 12f, 0f, 1f);
                color *= 0.6f + 0.4f * edge;

                // rows of an image go down, rows of a map go up
                image[px, size - 1 - py] = Pixel(color);
            }
        }

        return image;
    }
}
