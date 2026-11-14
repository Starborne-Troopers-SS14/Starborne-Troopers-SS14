using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._ST14.MainHub;

/// <summary>
/// Layered animated background for the main hub. Coordinates use a canvas of <see cref="CanvasSize"/> with y pointing down,
/// the layout follows Unity rect transforms: anchors on the parent rect, position of the pivot from the anchor point, size delta
/// </summary>
[Prototype("st14HubScene")]
public sealed partial class ST14HubScenePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Locale key of the name of the background, shown in the lobby
    /// </summary>
    [DataField]
    public LocId? Title { get; private set; }

    /// <summary>
    /// Locale key of the author or the source of the art, shown next to the name
    /// </summary>
    [DataField]
    public LocId? Artist { get; private set; }

    /// <summary>
    /// Length of the keyframed animation in seconds, curves hold their last value after it
    /// </summary>
    [DataField]
    public float Duration { get; private set; }

    [DataField]
    public Vector2 CanvasSize { get; private set; } = new(1920, 1080);

    /// <summary>
    /// Origin for layers without a rect, relative to the canvas
    /// </summary>
    [DataField]
    public Vector2 Pivot { get; private set; } = new(0.5f, 0.5f);

    /// <summary>
    /// Drawn in order, parents must come before their children
    /// </summary>
    [DataField]
    public List<ST14HubSceneLayer> Layers { get; private set; } = new();
}

[DataDefinition]
public sealed partial class ST14HubSceneLayer
{
    [DataField(required: true)]
    public string Id = default!;

    [DataField]
    public string? Parent;

    /// <summary>
    /// False for plain transforms that only group and move children, they have no size of their own
    /// </summary>
    [DataField]
    public bool Rect = true;

    [DataField]
    public ResPath? Texture;

    /// <summary>
    /// Fill the rect with <see cref="Color"/> when there is no texture
    /// </summary>
    [DataField]
    public bool Solid;

    /// <summary>
    /// Repeat the texture at its own size instead of stretching it
    /// </summary>
    [DataField]
    public bool Tile;

    [DataField]
    public Color Color = Color.White;

    [DataField]
    public Vector2 AnchorMin = new(0.5f, 0.5f);

    [DataField]
    public Vector2 AnchorMax = new(0.5f, 0.5f);

    [DataField]
    public Vector2 Position;

    [DataField]
    public Vector2 Size;

    [DataField]
    public Vector2 Pivot = new(0.5f, 0.5f);

    /// <summary>
    /// Degrees, clockwise
    /// </summary>
    [DataField]
    public float Rotation;

    [DataField]
    public Vector2 Scale = Vector2.One;

    [DataField]
    public ST14HubSceneText? Text;

    [DataField]
    public ST14HubSceneWiggle? Wiggle;

    [DataField]
    public ST14HubSceneDrift? Drift;

    [DataField]
    public List<ST14HubSceneCurve> Curves = new();
}

/// <summary>
/// Big slanted slogan drawn inside the layer rect
/// </summary>
[DataDefinition]
public sealed partial class ST14HubSceneText
{
    [DataField(required: true)]
    public LocId Loc;

    [DataField]
    public int Size = 64;
}

/// <summary>
/// Endless smooth random shake, like the Wiggle script of the original menu
/// </summary>
[DataDefinition]
public sealed partial class ST14HubSceneWiggle
{
    [DataField]
    public Vector2 Amplitude;

    [DataField]
    public Vector2 Frequency;

    [DataField]
    public float Rotation;

    [DataField]
    public float RotationFrequency;
}

/// <summary>
/// Very slow sine motion: the layer grows and shrinks a little and slides a little to the side, without any shake or rotation
/// </summary>
[DataDefinition]
public sealed partial class ST14HubSceneDrift
{
    /// <summary>
    /// Scale change in each direction, 0.015 means 98.5% to 101.5%
    /// </summary>
    [DataField]
    public float Breathe;

    [DataField]
    public float BreathePeriod = 25f;

    /// <summary>
    /// Largest shift in canvas pixels
    /// </summary>
    [DataField]
    public Vector2 Offset;

    [DataField]
    public float OffsetPeriod = 40f;

    /// <summary>
    /// Start of the cycle as a fraction of the period, so layers do not move in step
    /// </summary>
    [DataField]
    public float Phase;

    /// <summary>
    /// Straight run across the screen in canvas pixels per second. The layer covers Run * RunPeriod pixels
    /// centred on its position, then starts over from the other side, so it should be off screen at both ends
    /// </summary>
    [DataField]
    public Vector2 Run;

    [DataField]
    public float RunPeriod = 10f;
}

[DataDefinition]
public sealed partial class ST14HubSceneCurve
{
    [DataField(required: true)]
    public ST14HubSceneProperty Property;

    /// <summary>
    /// Cubic segments packed by five: start time, a, b, c, d. value = ((a * dt + b) * dt + c) * dt + d
    /// </summary>
    [DataField(required: true)]
    public List<float> Keys = new();

    /// <param name="extrapolate">
    /// Past the last key keep following the previous segment instead of holding the last value, so motion never stops dead
    /// </param>
    public float Evaluate(float time, bool extrapolate = false)
    {
        var segment = 0;
        for (var i = 5; i < Keys.Count; i += 5)
        {
            if (Keys[i] > time)
                break;

            // the last key only holds the end value, its previous segment carries the motion
            if (extrapolate && i + 5 >= Keys.Count)
                break;

            segment = i;
        }

        var dt = MathF.Max(0f, time - Keys[segment]);
        return ((Keys[segment + 1] * dt + Keys[segment + 2]) * dt + Keys[segment + 3]) * dt + Keys[segment + 4];
    }
}

public enum ST14HubSceneProperty : byte
{
    PosX,
    PosY,
    SizeX,
    SizeY,
    Rotation,
    PivotX,
    PivotY,
    ScaleX,
    ScaleY,
    Alpha,
}
