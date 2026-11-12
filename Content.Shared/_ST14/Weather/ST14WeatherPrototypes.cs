using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Weather;

/// <summary>
/// When in the day an event can happen in a sector
/// </summary>
public enum ST14WeatherTime : byte
{
    Any,
    Day,
    Night,
}

/// <summary>
/// A weather event or a natural disaster in a sector round: it starts on one sector map, lasts a while and does what its
/// effects say to everyone on that map
/// </summary>
[Prototype("st14WeatherEvent")]
public sealed partial class ST14WeatherEventPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>
    /// Told to everyone on the map when the event starts
    /// </summary>
    [DataField(required: true)]
    public LocId Warning;

    /// <summary>
    /// Told to everyone on the map when the event is over
    /// </summary>
    [DataField]
    public LocId? Over;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public TimeSpan MinDuration = TimeSpan.FromSeconds(90);

    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromSeconds(180);

    [DataField]
    public ST14WeatherTime Time = ST14WeatherTime.Any;

    /// <summary>
    /// How long the event takes to build up from nothing to its full strength. It starts gently and grows faster
    /// </summary>
    [DataField]
    public TimeSpan RampUp = TimeSpan.FromSeconds(50);

    /// <summary>
    /// How long it dies down before it ends
    /// </summary>
    [DataField]
    public TimeSpan RampDown = TimeSpan.FromSeconds(25);

    /// <summary>
    /// The one looping sound of the event, its volume follows the strength
    /// </summary>
    [DataField]
    public SoundSpecifier? Ambient;

    [DataField]
    public ST14WeatherVisual Visual = new();

    /// <summary>
    /// Played to everyone on the map when the event starts
    /// </summary>
    [DataField]
    public SoundSpecifier? StartSound;

    /// <summary>
    /// Change of the movement speed of everyone under the open sky, 1 is none
    /// </summary>
    [DataField]
    public float Speed = 1f;

    [DataField]
    public List<ST14WeatherEffect> Effects = new();

    /// <summary>
    /// Strength of the event at a time, 0 to 1: it builds up with an ease-in, holds, then dies down
    /// </summary>
    public float Strength(TimeSpan now, TimeSpan start, TimeSpan end)
    {
        if (now <= start || now >= end)
            return 0f;

        var up = RampUp <= TimeSpan.Zero ? 1f : (float) Math.Clamp((now - start) / RampUp, 0, 1);
        var down = RampDown <= TimeSpan.Zero ? 1f : (float) Math.Clamp((end - now) / RampDown, 0, 1);
        // a slow start that gathers pace, then a soft landing on the full strength
        up = up * up * (3 - 2 * up) * (0.35f + 0.65f * up);
        down = down * down * (3 - 2 * down);
        return MathF.Min(up, down);
    }
}

public enum ST14WeatherParticles : byte
{
    None,
    Rain,
    Hail,
    Snow,
    Ash,
    Sand,
    Dust,
    Motes,
}

/// <summary>
/// How an event looks on the screens of the people on the map: particles, drifting clouds and a tint, all growing with
/// the strength of the event
/// </summary>
[DataDefinition]
public sealed partial class ST14WeatherVisual
{
    [DataField]
    public ST14WeatherParticles Particles = ST14WeatherParticles.None;

    /// <summary>
    /// Particles on a screen of about 24 by 14 tiles at full strength
    /// </summary>
    [DataField]
    public int Count;

    [DataField]
    public Color Color = Color.White;

    /// <summary>
    /// Speed of the particles, tiles a second
    /// </summary>
    [DataField]
    public float Speed = 8f;

    /// <summary>
    /// Length of a streak, tiles
    /// </summary>
    [DataField]
    public float Length = 0.6f;

    [DataField]
    public float Width = 0.05f;

    /// <summary>
    /// Glowing sparks over the particles: embers of the ash, falling stars of a meteor shower
    /// </summary>
    [DataField]
    public int Embers;

    [DataField]
    public Color EmberColor = Color.FromHex("#FF9A40");

    /// <summary>
    /// Embers that cross the sky as long falling streaks
    /// </summary>
    [DataField]
    public bool Meteors;

    /// <summary>
    /// Thickness of the drifting clouds at full strength, 0 to 1
    /// </summary>
    [DataField]
    public float Fog;

    [DataField]
    public Color FogColor = Color.FromHex("#B49A78");

    /// <summary>
    /// Colour of every second cloud layer, the same as <see cref="FogColor"/> when not set. Two colours give the
    /// clouds depth: dark masses under light veils
    /// </summary>
    [DataField]
    public Color? FogColor2;

    /// <summary>
    /// Kinds of clouds of the layers, one per layer, at most four: puffy, billow, wisps, curls, patches, veil,
    /// ribbons. Wisps and ribbons are drawn along the wind
    /// </summary>
    [DataField]
    public List<string> Clouds = new() { "billow", "puffy", "curls" };

    /// <summary>
    /// How fast the clouds drift with the wind, tiles a second
    /// </summary>
    [DataField]
    public float FogSpeed = 2f;

    /// <summary>
    /// Colour laid over the whole view at full strength, its alpha is the strength
    /// </summary>
    [DataField]
    public Color Tint = Color.Transparent;
}

/// <summary>
/// Rules of the weather of the sector rounds
/// </summary>
[Prototype("st14WeatherSettings")]
public sealed partial class ST14WeatherSettingsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Quiet time on every sector after the round starts
    /// </summary>
    [DataField]
    public TimeSpan FirstDelay = TimeSpan.FromMinutes(4);

    [DataField]
    public TimeSpan MinGap = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan MaxGap = TimeSpan.FromMinutes(12);

    /// <summary>
    /// How often the light of the sky of every sector follows the star
    /// </summary>
    [DataField]
    public TimeSpan LightInterval = TimeSpan.FromSeconds(1);
}

/// <summary>
/// One thing a weather event does over and over while it lasts
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class ST14WeatherEffect
{
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Chance that the effect happens on one interval
    /// </summary>
    [DataField]
    public float Chance = 1f;
}

/// <summary>
/// The ground shakes: the screens of everyone on the map shake and some fall over
/// </summary>
public sealed partial class ST14WeatherShakeEffect : ST14WeatherEffect
{
    [DataField]
    public float Strength = 1f;

    [DataField]
    public float KnockdownChance;

    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.FromSeconds(2);
}

/// <summary>
/// The ground splits: cracks open near the people on the map while the event grows, and close again, one after
/// another, as it dies down. None is left when it is over
/// </summary>
public sealed partial class ST14WeatherFissureEffect : ST14WeatherEffect
{
    [DataField(required: true)]
    public List<string> Decals = new();

    /// <summary>
    /// Size of the crack decals in tiles, they are centred on the spot
    /// </summary>
    [DataField]
    public System.Numerics.Vector2 Size = new(6, 3);

    /// <summary>
    /// How far from a person a crack can open
    /// </summary>
    [DataField]
    public float Spread = 8f;

    /// <summary>
    /// Share of cracks that open anywhere on the map rather than near someone
    /// </summary>
    [DataField]
    public float Anywhere = 0.3f;

    /// <summary>
    /// Most cracks open at once on a map at full strength
    /// </summary>
    [DataField]
    public int Max = 40;

    [DataField]
    public SoundSpecifier? Sound;
}

/// <summary>
/// Something falls out of the sky near the people on the map: rocks, meteors, lightning. The strike entity shows
/// where it lands, then hits
/// </summary>
public sealed partial class ST14WeatherStrikeEffect : ST14WeatherEffect
{
    [DataField(required: true)]
    public EntProtoId Strike;

    [DataField]
    public int Count = 1;

    /// <summary>
    /// How far from a person the strike can land
    /// </summary>
    [DataField]
    public float Spread = 9f;

    /// <summary>
    /// Share of strikes that land anywhere on the map rather than near someone
    /// </summary>
    [DataField]
    public float Anywhere = 0.3f;
}

/// <summary>
/// Gusts of wind from one side, picked when the event starts: loose things fly and people stagger and fall
/// </summary>
public sealed partial class ST14WeatherGustEffect : ST14WeatherEffect
{
    [DataField]
    public float Distance = 4f;

    [DataField]
    public float ItemChance = 0.5f;

    [DataField]
    public float PushChance = 0.35f;

    [DataField]
    public float KnockdownChance = 0.1f;

    [DataField]
    public SoundSpecifier? Sound;
}

/// <summary>
/// Hurts everyone under the open sky
/// </summary>
public sealed partial class ST14WeatherExposureEffect : ST14WeatherEffect
{
    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public LocId? Popup;
}

/// <summary>
/// Puts out everyone burning under the open sky
/// </summary>
public sealed partial class ST14WeatherExtinguishEffect : ST14WeatherEffect;

/// <summary>
/// A sound heard all over the map, at a random place near someone: far thunder
/// </summary>
public sealed partial class ST14WeatherSoundEffect : ST14WeatherEffect
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField]
    public float Spread = 20f;
}
