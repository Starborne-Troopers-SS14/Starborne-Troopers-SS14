using System.Numerics;

namespace Content.Shared._ST14.Weather;

/// <summary>
/// The day and the night of a war planet. The planet turns about its north axis (+Y of the hex grid) once a day, the
/// star stands still, so the half of the planet that faces the star has the day and the other half the night. The turn
/// comes from the real clock: the server, every client and every round see the star in the same place
/// </summary>
public static class ST14PlanetDay
{
    /// <summary>
    /// How far the star stands above the equator, in radians: the north gets a little more day than the south
    /// </summary>
    public const float StarDeclination = 0.2f;

    /// <summary>
    /// Share of the day that has passed, 0 to 1, for a day of <paramref name="dayMinutes"/>
    /// </summary>
    public static float Phase(DateTime utcNow, float dayMinutes, float offset)
    {
        var length = Math.Max(1.0, dayMinutes) * 60.0;
        var seconds = utcNow.Ticks / (double) TimeSpan.TicksPerSecond;
        var phase = seconds / length + offset;
        return (float) (phase - Math.Floor(phase));
    }

    /// <summary>
    /// The direction to the star in the frame of the planet, unit length
    /// </summary>
    public static Vector3 StarDirection(float phase)
    {
        var angle = -phase * MathF.Tau;
        var flat = MathF.Cos(StarDeclination);
        return new Vector3(MathF.Cos(angle) * flat, MathF.Sin(StarDeclination), MathF.Sin(angle) * flat);
    }

    /// <summary>
    /// Sine of the height of the star over the horizon of a point of the planet: above 0 it is day there
    /// </summary>
    public static float StarHeight(Vector3 point, Vector3 star)
    {
        return Vector3.Dot(Vector3.Normalize(point), star);
    }

    /// <summary>
    /// The phase at which the star stands at the local hour <paramref name="hour"/> (0 to 24, 12 is noon) over a point
    /// </summary>
    public static float PhaseForHour(Vector3 point, float hour)
    {
        var longitude = MathF.Atan2(point.Z, point.X);
        var starLongitude = longitude - (hour - 12f) / 24f * MathF.Tau;
        var phase = -starLongitude / MathF.Tau;
        return phase - MathF.Floor(phase);
    }

    /// <summary>
    /// The local hour at a point of the planet, 0 to 24, 12 is noon
    /// </summary>
    public static float LocalHour(Vector3 point, Vector3 star)
    {
        var angle = MathF.Atan2(point.Z, point.X) - MathF.Atan2(star.Z, star.X);
        var hour = 12f + angle / MathF.Tau * 24f;
        return hour - MathF.Floor(hour / 24f) * 24f;
    }

    private static readonly (float Height, Color Color)[] Sky =
    {
        // deep night is pitch black: only lamps, flares and fires light the ground
        (-1f, Color.FromHex("#000000")),
        (-0.22f, Color.FromHex("#000000")),
        (-0.1f, Color.FromHex("#0C0C18")),
        (-0.02f, Color.FromHex("#3A2838")),
        (0.04f, Color.FromHex("#D27E52")),
        (0.12f, Color.FromHex("#E8B486")),
        (0.28f, Color.FromHex("#E6D6B8")),
        (1f, Color.FromHex("#F3EBD6")),
    };

    /// <summary>
    /// The light of the sky over a sector with the star at this height: night, dawn or dusk, day
    /// </summary>
    public static Color SkyLight(float height)
    {
        if (height <= Sky[0].Height)
            return Sky[0].Color;

        for (var i = 1; i < Sky.Length; i++)
        {
            if (height > Sky[i].Height)
                continue;

            var (h0, c0) = Sky[i - 1];
            var (h1, c1) = Sky[i];
            var t = (height - h0) / (h1 - h0);
            // smooth the joins so the light never jumps from one key to the next
            t = t * t * (3 - 2 * t);
            return Color.InterpolateBetween(c0, c1, t);
        }

        return Sky[^1].Color;
    }
}
