using System.Linq;
using System.Numerics;
using Content.Client._ST14.MainHub;
using Content.Client._ST14.MainHub.Galaxy;
using Content.Shared._ST14.Sectors;
using Robust.Client.Graphics;

namespace Content.Client._ST14.Sectors;

/// <summary>
/// The subsectors of the loaded sectors: each sector split between its resource points, every part tinted by the side
/// holding its point. Whoever holds all of them when the round ends takes the sector
/// </summary>
public sealed partial class ST14BattleMapView
{
    private static readonly string[] PointLetters = { "А", "Б", "В", "Г", "Д", "Е" };
    private static readonly Color Free = Color.FromHex("#C9C3B4");

    /// <summary>
    /// The part of the sector closer to the point than to any other point of the sector, in the tiles of the sector
    /// </summary>
    private static List<Vector2> Subsector(Vector2 point, IEnumerable<Vector2> others)
    {
        var polygon = new List<Vector2>();
        for (var i = 0; i < 6; i++)
        {
            var angle = MathF.PI / 6 + MathF.PI / 3 * i;
            polygon.Add(ST14SectorGeometry.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ST14SectorGeometry.Radius);
        }

        foreach (var other in others)
        {
            var normal = other - point;
            if (normal.LengthSquared() < 0.01f)
                continue;

            var middle = (point + other) / 2;
            var clipped = new List<Vector2>();
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var da = Vector2.Dot(a - middle, normal);
                var db = Vector2.Dot(b - middle, normal);
                if (da <= 0)
                    clipped.Add(a);

                if (da < 0 != db < 0)
                    clipped.Add(a + (b - a) * (da / (da - db)));
            }

            polygon = clipped;
        }

        return polygon;
    }

    private IEnumerable<IGrouping<int, ST14SubsectorPoint>> PointsByCell()
    {
        return _system.Live?.Points.Where(p => _layout.ContainsKey(p.Cell)).GroupBy(p => p.Cell) ?? Enumerable.Empty<IGrouping<int, ST14SubsectorPoint>>();
    }

    private void AddDashed(Vector2 from, Vector2 to, float width, Color color)
    {
        var length = (to - from).Length();
        if (length < 1f)
            return;

        var direction = (to - from) / length;
        const float dash = 7f, gap = 5f;
        for (var at = 0f; at < length; at += dash + gap)
        {
            _buffer.Line(from + direction * at, from + direction * MathF.Min(at + dash, length), width, color);
        }
    }

    /// <summary>
    /// The tinted parts and the dashed borders between them, laid over the ground
    /// </summary>
    private void AddSubsectors()
    {
        foreach (var group in PointsByCell())
        {
            var points = group.ToList();
            var line = ST14HubStyle.GoldLight.WithAlpha(0.9f);
            foreach (var point in points)
            {
                var polygon = Subsector(point.Position, points.Where(p => p.Position != point.Position).Select(p => p.Position))
                    .Select(v => ToScreen(ToWorld(group.Key, v) ?? Vector2.Zero)).ToList();
                if (polygon.Count < 3)
                    continue;

                var middle = polygon.Aggregate(Vector2.Zero, (sum, v) => sum + v) / polygon.Count;
                var tint = point.Holder is { } holder ? ST14GalaxyColors.Of(holder).WithAlpha(0.2f) : Color.Transparent;
                for (var i = 0; i < polygon.Count; i++)
                {
                    var a = polygon[i];
                    var b = polygon[(i + 1) % polygon.Count];
                    if (tint.A > 0)
                        _buffer.Triangle(middle, a, b, tint);

                    // the border of the sector is drawn by its outline, only the borders inside it are dashed
                    var edgeMiddle = ToLocalScreen(group.Key, (a + b) / 2);
                    if (edgeMiddle is { } local && EdgeDistance(local) > 1.5f)
                        AddDashed(a, b, 2.5f, line);
                }
            }
        }
    }

    /// <summary>
    /// A diamond on every point, in the colour of the side holding it
    /// </summary>
    private void AddPointMarks()
    {
        var size = Math.Clamp(5f * _zoom, 7f, 16f);
        foreach (var group in PointsByCell())
        {
            foreach (var point in group)
            {
                if (ToWorld(point.Cell, point.Position) is not { } world)
                    continue;

                var at = ToScreen(world);
                var color = point.Holder is { } holder ? ST14GalaxyColors.Of(holder) : Free;
                AddDiamond(at, size + 2.5f, Color.Black);
                AddDiamond(at, size, color);
                AddDiamond(at, size * 0.4f, Color.Black.WithAlpha(0.6f));
            }
        }
    }

    private void AddDiamond(Vector2 at, float size, Color color)
    {
        var up = new Vector2(0, -size);
        var right = new Vector2(size, 0);
        _buffer.Triangle(at + up, at + right, at - up, color);
        _buffer.Triangle(at + up, at - right, at - up, color);
    }

    private void DrawPointLetters(DrawingHandleScreen handle)
    {
        var font = ST14HubStyle.Font(_cache, ST14HubFont.SemiBold, Math.Clamp((int) (4f * _zoom), 11, 18));
        var size = Math.Clamp(5f * _zoom, 7f, 16f);
        foreach (var group in PointsByCell())
        {
            var index = 0;
            foreach (var point in group)
            {
                var letter = PointLetters[index++ % PointLetters.Length];
                if (ToWorld(point.Cell, point.Position) is { } world)
                    ST14HubStyle.DrawRotatedText(handle, font, letter, ToScreen(world) + new Vector2(0, size + font.GetLineHeight(1f) * 0.6f), 0, Color.White, ST14HubStyle.TextShadow);
            }
        }
    }

    /// <summary>
    /// A point on screen back in the tiles of the sector
    /// </summary>
    private Vector2? ToLocalScreen(int cell, Vector2 pixel)
    {
        return _layout.ContainsKey(cell) ? ToLocal(cell, ScreenToWorld(pixel)) : null;
    }

    private static float EdgeDistance(Vector2 local)
    {
        var offset = local - ST14SectorGeometry.Center;
        var far = 0f;
        for (var i = 0; i < 6; i++)
        {
            far = MathF.Max(far, Vector2.Dot(offset, ST14SectorGeometry.EdgeNormal(i)));
        }

        return ST14SectorGeometry.Apothem - far;
    }
}
