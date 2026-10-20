using System.Numerics;
using Content.Client.Clickable;
using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Random;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14MultitoolConeSystem : EntitySystem
{
    [Dependency] private IClickMapManager _clickMaps = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14MultitoolSystem _multitool = default!;

    public const float SparkLifetime = 0.5f;
    private const float SparksPerSecond = 24f;
    private const float SparkGravity = 200f;
    private const int MaxSparks = 256;

    public readonly List<ST14Spark> Sparks = new();
    private readonly Dictionary<(RSI, RSI.StateId, RsiDirection, int), (int Left, int Right)?> _rows = new();

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new ST14MultitoolConeOverlay(this));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<ST14MultitoolConeOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        Sparks.RemoveAll(spark => spark.Age >= SparkLifetime || !Exists(spark.Blueprint));
        foreach (var spark in Sparks)
        {
            spark.Age += frameTime;
            spark.Velocity.Y -= SparkGravity * frameTime;
            spark.Position += spark.Velocity * frameTime;
        }

        var eyeRotation = _eye.CurrentEye.Rotation;
        var query = EntityQueryEnumerator<ST14BlueprintComponent, ST14BlueprintVisualsComponent>();
        while (query.MoveNext(out var uid, out var blueprint, out var visuals))
        {
            if (blueprint.Speed == 0f || !visuals.Sparks || !TryGetLine(uid, eyeRotation, out var line))
                continue;

            var amount = frameTime * SparksPerSecond;
            var count = (int) amount;
            if (_random.Prob(amount - count))
                count++;

            for (var i = 0; i < count && Sparks.Count < MaxSparks; i++)
            {
                if (!TryPickSparkOrigin(line, out var origin))
                    continue;

                Sparks.Add(new ST14Spark
                {
                    Blueprint = uid,
                    Position = origin,
                    Velocity = new Vector2(_random.NextFloat(-24f, 24f), _random.NextFloat(20f, 45f)),
                });
            }
        }
    }

    public bool TryGetSpriteMatrix(EntityUid uid, Angle eyeRotation, out Matrix3x2 matrix)
    {
        matrix = default;
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return false;

        var (position, rotation) = _transform.GetWorldPositionRotation(uid);
        var world = Matrix3Helpers.CreateTransform(position, sprite.NoRotation ? -eyeRotation : rotation);

        matrix = Matrix3x2.CreateScale(1f / EyeManager.PixelsPerMeter) * sprite.LocalMatrix * world;
        return true;
    }

    public bool TryGetCone(ST14MultitoolComponent tool, Angle eyeRotation, out ST14Cone cone)
    {
        cone = default;

        if (tool.Mode == ST14MultitoolMode.None || tool.Target is not { } target || tool.User is not { } user)
            return false;

        if (Transform(user).MapID != Transform(target).MapID ||
            !TryGetLine(target, eyeRotation, out var line) ||
            !Matrix3x2.Invert(line.Matrix, out var inverse))
            return false;

        var basePos = line.ToSprite((line.Left + line.Right + 1) / 2f, line.Row + 0.5f);
        var radiusX = (line.Right - line.Left + 1) / 2f + 1f;
        var radius = new Vector2(radiusX, Math.Clamp(radiusX * 0.25f, 3f, 10f));

        var userPos = _transform.GetWorldPosition(user);
        var toBase = Vector2.Transform(basePos, line.Matrix) - userPos;
        var tip = toBase.LengthSquared() > 0.0001f ? userPos + Vector2.Normalize(toBase) * 0.3f : userPos;

        cone = new ST14Cone(target, line.Matrix, Vector2.Transform(tip, inverse), basePos, radius, tool.Mode);
        return true;
    }

    private bool TryGetLine(EntityUid uid, Angle eyeRotation, out BlueprintLine line)
    {
        line = default;

        if (!TryComp<ST14BlueprintComponent>(uid, out var blueprint) ||
            !TryComp<ST14BlueprintVisualsComponent>(uid, out var visuals) ||
            !TryComp<SpriteComponent>(uid, out var sprite) ||
            visuals.MainLayer < 0)
            return false;

        if (sprite[visuals.MainLayer] is not SpriteComponent.Layer { ActualRsi: { } rsi } layer ||
            !rsi.TryGetState(layer.State, out var state) ||
            !TryGetSpriteMatrix(uid, eyeRotation, out var matrix))
            return false;

        var rotation = (_transform.GetWorldRotation(uid) + eyeRotation).Reduced().FlipPositive();
        var dir = SpriteComponent.Layer.GetDirection(state.RsiDirections, rotation);
        var progressRow = (int) (_multitool.GetProgress(blueprint) * rsi.Size.Y);

        if (FindRow(rsi, layer.State, dir, progressRow) is not { } row)
            return false;

        line = new BlueprintLine(matrix, rsi, layer.State, dir, row.Row, row.Left, row.Right);
        return true;
    }

    private bool TryPickSparkOrigin(BlueprintLine line, out Vector2 origin)
    {
        origin = default;
        var y = line.Rsi.Size.Y - 1 - line.Row;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var x = _random.Next(line.Left, line.Right + 1);
            if (!_clickMaps.IsOccluding(line.Rsi, line.State, line.Dir, 0, new Vector2i(x, y)))
                continue;

            origin = line.ToSprite(x + 0.5f, line.Row + 0.5f);
            return true;
        }

        return false;
    }

    private (int Row, int Left, int Right)? FindRow(RSI rsi, RSI.StateId state, RsiDirection dir, int row)
    {
        for (var step = 0; step < rsi.Size.Y; step++)
        {
            if (GetRow(rsi, state, dir, row - step) is { } below)
                return (row - step, below.Left, below.Right);

            if (GetRow(rsi, state, dir, row + step) is { } above)
                return (row + step, above.Left, above.Right);
        }

        return null;
    }

    private (int Left, int Right)? GetRow(RSI rsi, RSI.StateId state, RsiDirection dir, int row)
    {
        if (row < 0 || row >= rsi.Size.Y)
            return null;

        if (_rows.TryGetValue((rsi, state, dir, row), out var cached))
            return cached;

        var y = rsi.Size.Y - 1 - row;
        var left = -1;
        var right = -1;

        for (var x = 0; x < rsi.Size.X; x++)
        {
            if (!_clickMaps.IsOccluding(rsi, state, dir, 0, new Vector2i(x, y)))
                continue;

            if (left < 0)
                left = x;
            right = x;
        }

        (int Left, int Right)? result = null;
        if (left >= 0)
        {
            if (right - left > 4)
                result = (left + 2, right - 2);
            else
                result = (left, right);
        }

        _rows[(rsi, state, dir, row)] = result;
        return result;
    }

    private readonly record struct BlueprintLine(
        Matrix3x2 Matrix,
        RSI Rsi,
        RSI.StateId State,
        RsiDirection Dir,
        int Row,
        int Left,
        int Right)
    {
        public Vector2 ToSprite(float x, float y)
        {
            return new Vector2(x - Rsi.Size.X / 2f, y - Rsi.Size.Y / 2f);
        }
    }
}

public readonly record struct ST14Cone(
    EntityUid Blueprint,
    Matrix3x2 Matrix,
    Vector2 Tip,
    Vector2 Base,
    Vector2 Radius,
    ST14MultitoolMode Mode);

public sealed class ST14Spark
{
    public EntityUid Blueprint;
    public Vector2 Position;
    public Vector2 Velocity;
    public float Age;
}
