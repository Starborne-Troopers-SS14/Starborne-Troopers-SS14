using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14MultitoolConeOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color BuildColor = Color.FromSrgb(Color.FromHex("#59e6ff"));
    private static readonly Color DismantleColor = Color.FromSrgb(Color.FromHex("#ff6a4d"));
    private static readonly Color SparkHot = Color.FromSrgb(Color.FromHex("#fff299"));
    private static readonly Color SparkCold = Color.FromSrgb(Color.FromHex("#ff731a"));

    private const int Segments = 24;
    private const int Slices = 16;

    private readonly ST14MultitoolConeSystem _cones;
    private readonly Vector2[] _arc = new Vector2[Segments + 1];
    private readonly List<DrawVertexUV2DColor> _fill = new();
    private readonly List<DrawVertexUV2DColor> _lines = new();
    private readonly List<DrawVertexUV2DColor> _sparks = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14MultitoolConeOverlay(ST14MultitoolConeSystem cones)
    {
        IoCManager.InjectDependencies(this);
        _cones = cones;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var time = (float) _timing.RealTime.TotalSeconds;

        _fill.Clear();
        _lines.Clear();
        _sparks.Clear();

        var query = _entMan.EntityQueryEnumerator<ST14MultitoolComponent>();
        while (query.MoveNext(out _, out var tool))
        {
            if (_cones.TryGetCone(tool, eyeRotation, out var cone) && OnMap(cone.Blueprint, args.MapId))
                AddCone(cone, time);
        }

        foreach (var spark in _cones.Sparks)
        {
            if (OnMap(spark.Blueprint, args.MapId) && _cones.TryGetSpriteMatrix(spark.Blueprint, eyeRotation, out var matrix))
                AddSpark(spark, matrix);
        }

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        Flush(handle, DrawPrimitiveTopology.TriangleList, _fill);
        Flush(handle, DrawPrimitiveTopology.LineList, _lines);
        Flush(handle, DrawPrimitiveTopology.TriangleList, _sparks);
    }

    private static void Flush(DrawingHandleWorld handle, DrawPrimitiveTopology topology, List<DrawVertexUV2DColor> vertices)
    {
        if (vertices.Count > 0)
            handle.DrawPrimitives(topology, Texture.White, CollectionsMarshal.AsSpan(vertices));
    }

    private bool OnMap(EntityUid uid, MapId map)
    {
        return _entMan.TryGetComponent(uid, out TransformComponent? xform) && xform.MapID == map;
    }

    private void AddCone(ST14Cone cone, float time)
    {
        var color = cone.Mode == ST14MultitoolMode.Dismantle ? DismantleColor : BuildColor;
        var edge = Color.InterpolateBetween(color, Color.White, 0.6f);
        var flicker = 0.75f + 0.25f * MathF.Sin(time * 35f);

        var tip = (cone.Tip - cone.Base) / cone.Radius;
        var outside = tip.Length() > 1f;
        var half = outside ? MathF.Acos(1f / tip.Length()) : 0f;
        var start = MathF.Atan2(tip.Y, tip.X) + half;

        for (var i = 0; i <= Segments; i++)
        {
            _arc[i] = OnEllipse(cone, start + (MathF.Tau - 2f * half) * i / Segments);
        }

        for (var slice = 0; slice < Slices; slice++)
        {
            var near = (float) slice / Slices;
            var far = (float) (slice + 1) / Slices;

            for (var i = 0; i < Segments; i++)
            {
                AddQuad(_fill, cone.Matrix,
                    Vector2.Lerp(cone.Tip, _arc[i], near),
                    Vector2.Lerp(cone.Tip, _arc[i + 1], near),
                    Vector2.Lerp(cone.Tip, _arc[i + 1], far),
                    Vector2.Lerp(cone.Tip, _arc[i], far),
                    color.WithAlpha(SliceAlpha(near, time)),
                    color.WithAlpha(SliceAlpha(far, time)));
            }
        }

        if (outside)
        {
            AddLine(cone.Matrix, cone.Tip, _arc[0], edge.WithAlpha(0.5f * flicker));
            AddLine(cone.Matrix, cone.Tip, _arc[Segments], edge.WithAlpha(0.5f * flicker));
        }

        var step = MathF.Tau / (Segments * 2);
        for (var i = 0; i < Segments * 2; i++)
        {
            AddLine(cone.Matrix, OnEllipse(cone, i * step), OnEllipse(cone, (i + 1) * step), edge.WithAlpha(0.75f * flicker));
        }
    }

    private static Vector2 OnEllipse(ST14Cone cone, float angle)
    {
        return cone.Base + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * cone.Radius;
    }

    private static float SliceAlpha(float along, float time)
    {
        return 0.07f + 0.2f * along + 0.08f * MathF.Max(0f, MathF.Sin(along * 12f - time * 10f));
    }

    private void AddSpark(ST14Spark spark, Matrix3x2 matrix)
    {
        var life = spark.Age / ST14MultitoolConeSystem.SparkLifetime;
        var color = Color.InterpolateBetween(SparkHot, SparkCold, life);

        for (var trail = 0; trail < 3; trail++)
        {
            var position = spark.Position - spark.Velocity * (trail * 0.025f);
            var pixel = new Vector2(MathF.Floor(position.X), MathF.Floor(position.Y));
            var tint = color.WithAlpha((1f - trail * 0.3f) * (1f - 0.5f * life));

            AddQuad(_sparks, matrix, pixel, pixel + Vector2.UnitX, pixel + Vector2.One, pixel + Vector2.UnitY, tint, tint);
        }
    }

    private void AddLine(Matrix3x2 matrix, Vector2 from, Vector2 to, Color color)
    {
        _lines.Add(new DrawVertexUV2DColor(Vector2.Transform(from, matrix), color));
        _lines.Add(new DrawVertexUV2DColor(Vector2.Transform(to, matrix), color));
    }

    private static void AddQuad(List<DrawVertexUV2DColor> list, Matrix3x2 matrix,
        Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color nearColor, Color farColor)
    {
        var va = new DrawVertexUV2DColor(Vector2.Transform(a, matrix), nearColor);
        var vb = new DrawVertexUV2DColor(Vector2.Transform(b, matrix), nearColor);
        var vc = new DrawVertexUV2DColor(Vector2.Transform(c, matrix), farColor);
        var vd = new DrawVertexUV2DColor(Vector2.Transform(d, matrix), farColor);

        list.Add(va);
        list.Add(vb);
        list.Add(vc);
        list.Add(va);
        list.Add(vc);
        list.Add(vd);
    }
}
