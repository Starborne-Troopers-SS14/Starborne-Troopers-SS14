using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._ST14.EnergyFence;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._ST14.ElectricFence;

/// <summary>
/// Draws the burning arcs of the fence. Every frame the path is built anew: from pylon A, through every wrapped bug
/// in the order they stand along the link (into its loop and once or twice around its body), to pylon B. The path is cut
/// into short pieces shaken along their normals, more on long straight runs, less on the loops,
/// and drawn as quads in three passes (wide faint blue, middle light blue, thin white core). Every other frame
/// a second faint arc with its own shake flickers next to the first
/// </summary>
public sealed partial class ST14FenceArcOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;

    private static readonly Color GlowColor = Color.FromSrgb(new Color(0.31f, 0.70f, 1f, 0.16f));
    private static readonly Color MiddleColor = Color.FromSrgb(new Color(0.66f, 0.89f, 1f, 0.55f));
    private static readonly Color CoreColor = Color.FromSrgb(new Color(1f, 1f, 1f, 0.95f));

    private const float GlowWidth = 0.30f;
    private const float MiddleWidth = 0.12f;
    private const float CoreWidth = 0.04f;

    /// <summary>
    /// Length of one shaken piece of a straight run, in tiles
    /// </summary>
    private const float StepLength = 0.22f;

    /// <summary>
    /// Angle of one piece of a loop
    /// </summary>
    private const float LoopStep = MathF.PI / 10f;

    private readonly SharedTransformSystem _transform;
    private readonly Random _random = new();
    private readonly List<Vector2> _path = new();
    private readonly List<(Vector2 Pos, float Depth, float T)> _bugs = new();
    private readonly List<DrawVertexUV2DColor> _vertices = new();

    private int _frame;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public ST14FenceArcOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entMan.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _frame++;
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        // Pylon emitters are offset in screen space, like the noRot sprite they sit on
        var eyeRotation = args.Viewport.Eye?.Rotation ?? Angle.Zero;

        var query = _entMan.EntityQueryEnumerator<ST14EnergyPylonComponent, TransformComponent>();
        while (query.MoveNext(out _, out var pylon, out var xform))
        {
            if (xform.MapID != args.MapId || pylon.Links.Count == 0)
                continue;

            foreach (var link in pylon.Links)
            {
                if (!link.Active
                    || !_entMan.TryGetEntity(link.Other, out var other)
                    || !_entMan.TryGetComponent(other, out TransformComponent? otherXform)
                    || otherXform.MapID != args.MapId)
                {
                    continue;
                }

                var offset = (-eyeRotation).RotateVec(pylon.ArcOffset);
                var a = _transform.GetWorldPosition(xform) + offset;
                var b = _transform.GetWorldPosition(otherXform) + offset;

                CollectBugs(link, a, b);

                BuildPath(a, b, pylon, 1f);
                DrawPath(handle, 1f);

                if (_frame % 2 == 0)
                {
                    BuildPath(a, b, pylon, 1.6f);
                    DrawPath(handle, 0.35f);
                }
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    /// Wrapped bugs of the link sorted by where they stand along it
    /// </summary>
    private void CollectBugs(ST14FenceLinkState link, Vector2 a, Vector2 b)
    {
        _bugs.Clear();
        var ab = b - a;
        var lengthSquared = MathF.Max(ab.LengthSquared(), 0.0001f);

        for (var i = 0; i < link.Wrapped.Length; i++)
        {
            if (!_entMan.TryGetEntity(link.Wrapped[i], out var bug)
                || !_entMan.TryGetComponent(bug, out TransformComponent? bugXform))
            {
                continue;
            }

            var pos = _transform.GetWorldPosition(bugXform);
            var t = Vector2.Dot(pos - a, ab) / lengthSquared;
            _bugs.Add((pos, link.Depths[i], t));
        }

        _bugs.Sort((x, y) => x.T.CompareTo(y.T));
    }

    private void BuildPath(Vector2 a, Vector2 b, ST14EnergyPylonComponent pylon, float shake)
    {
        _path.Clear();
        _path.Add(a);

        var line = b - a;
        for (var i = 0; i < _bugs.Count; i++)
        {
            var (center, depth, _) = _bugs[i];
            var radius = pylon.LoopRadius;
            var previous = _path[^1];
            var next = i + 1 < _bugs.Count ? _bugs[i + 1].Pos : b;

            var enterAngle = AngleOf(previous - center);
            var exitAngle = AngleOf(next - center);

            // The arc runs round the bug in the sense that matches the side of the line it stands on
            var side = line.X * (center.Y - a.Y) - line.Y * (center.X - a.X);
            var ccw = side >= 0f;
            var loops = depth > pylon.DoubleLoopDepth ? 2 : 1;

            var delta = exitAngle - enterAngle;
            if (ccw)
                delta = Wrap(delta);
            else
                delta = -Wrap(-delta);

            var sweep = (ccw ? 1f : -1f) * loops * MathF.Tau + delta;

            AddStraight(center + Direction(enterAngle) * radius, shake);
            AddLoop(center, radius, enterAngle, sweep, shake);
        }

        AddStraight(b, shake);
    }

    private void AddStraight(Vector2 to, float shake)
    {
        var from = _path[^1];
        var run = to - from;
        var length = run.Length();
        if (length < 0.001f)
            return;

        var normal = new Vector2(-run.Y, run.X) / length;
        var steps = Math.Max(2, (int) MathF.Ceiling(length / StepLength));
        var amplitude = Math.Clamp(0.05f + 0.045f * length, 0.05f, 0.32f) * shake;

        for (var k = 1; k <= steps; k++)
        {
            var t = k / (float) steps;
            var point = from + run * t;
            if (k < steps)
            {
                // Pinned at both ends, wildest in the middle
                var envelope = MathF.Sin(MathF.PI * t);
                point += normal * NextSigned() * amplitude * envelope;
            }

            _path.Add(point);
        }
    }

    private void AddLoop(Vector2 center, float radius, float start, float sweep, float shake)
    {
        var steps = Math.Max(4, (int) MathF.Ceiling(MathF.Abs(sweep) / LoopStep));
        for (var k = 1; k <= steps; k++)
        {
            var angle = start + sweep * k / steps;
            var r = radius + NextSigned() * 0.05f * shake;
            _path.Add(center + Direction(angle) * r);
        }
    }

    private void DrawPath(DrawingHandleWorld handle, float alpha)
    {
        DrawPass(handle, GlowWidth, GlowColor, alpha);
        DrawPass(handle, MiddleWidth, MiddleColor, alpha);
        DrawPass(handle, CoreWidth, CoreColor, alpha);
    }

    private void DrawPass(DrawingHandleWorld handle, float width, Color color, float alpha)
    {
        _vertices.Clear();
        color = color.WithAlpha(color.A * alpha);
        var half = width / 2f;

        for (var i = 0; i + 1 < _path.Count; i++)
        {
            var p0 = _path[i];
            var p1 = _path[i + 1];
            var dir = p1 - p0;
            var length = dir.Length();
            if (length < 0.0001f)
                continue;

            // Stretched a little past both ends so the joints have no gaps
            dir /= length;
            var normal = new Vector2(-dir.Y, dir.X) * half;
            var extend = dir * half * 0.5f;
            var s = p0 - extend;
            var e = p1 + extend;

            var v0 = new DrawVertexUV2DColor(s + normal, Vector2.Zero, color);
            var v1 = new DrawVertexUV2DColor(s - normal, Vector2.Zero, color);
            var v2 = new DrawVertexUV2DColor(e + normal, Vector2.Zero, color);
            var v3 = new DrawVertexUV2DColor(e - normal, Vector2.Zero, color);

            _vertices.Add(v0);
            _vertices.Add(v1);
            _vertices.Add(v2);
            _vertices.Add(v1);
            _vertices.Add(v3);
            _vertices.Add(v2);
        }

        if (_vertices.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White, CollectionsMarshal.AsSpan(_vertices));
    }

    private float NextSigned()
    {
        return (float) (_random.NextDouble() * 2.0 - 1.0);
    }

    private static float AngleOf(Vector2 v)
    {
        return MathF.Atan2(v.Y, v.X);
    }

    private static Vector2 Direction(float angle)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    /// <summary>
    /// Angle into [0, 2π)
    /// </summary>
    private static float Wrap(float angle)
    {
        angle %= MathF.Tau;
        return angle < 0f ? angle + MathF.Tau : angle;
    }
}
