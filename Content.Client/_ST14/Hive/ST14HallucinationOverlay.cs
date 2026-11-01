using System.Numerics;
using Content.Shared._ST14.Hive.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Visions the brain bug puts in a trooper's head: the screen breathes red and violet, the edges sink into a dark
/// full of eyes, bugs that are not there loom up and rush at the middle, and the picture tears in flickering bars.
/// The intensity of the visions sets how much of it there is
/// </summary>
public sealed partial class ST14HallucinationOverlay : Overlay
{
    // Visions fade in and out over this long instead of popping
    private const float FadeTime = 2f;

    private static readonly ResPath Bugs = new("/Textures/_ST14/Mobs/Bugs");
    private static readonly string[] Phantoms = { "warrior.rsi", "tiger.rsi", "brain.rsi", "larva.rsi" };

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;

    private readonly SpriteSystem _sprite;

    // made on the first draw: a client without graphics, like the test one, never needs them
    private Texture[]? _phantoms;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public ST14HallucinationOverlay()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entities.System<SpriteSystem>();
        ZIndex = 50;
    }

    /// <summary>
    /// How strong the visions of the local player are right now, faded at both ends. Zero without visions
    /// </summary>
    public float Strength()
    {
        if (!_entities.TryGetComponent(_player.LocalEntity, out ST14HallucinatingComponent? visions))
            return 0f;

        var left = (float) (visions.End - _timing.CurTime).TotalSeconds;
        return visions.Intensity * Math.Clamp(left / FadeTime, 0f, 1f);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return Strength() > 0.01f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var strength = Strength();
        var handle = args.ScreenHandle;
        var screen = new UIBox2(args.ViewportBounds.Left, args.ViewportBounds.Top, args.ViewportBounds.Right, args.ViewportBounds.Bottom);
        var time = (float) _timing.RealTime.TotalSeconds;

        _phantoms ??= LoadPhantoms();

        // the whole screen breathes between blood red and bruise violet
        var breath = 0.5f + 0.5f * MathF.Sin(time * 1.7f);
        var wash = Color.InterpolateBetween(new Color(0.6f, 0f, 0.05f), new Color(0.35f, 0f, 0.5f), breath);
        handle.DrawRect(screen, wash.WithAlpha(0.12f * strength + 0.06f * strength * MathF.Sin(time * 5.3f)));

        DrawPhantoms(handle, screen, time, strength);
        DrawVignette(handle, screen, time, strength);
        DrawEyes(handle, screen, time, strength);
        DrawTears(handle, screen, time, strength);

        // now and then the whole picture blinks dark
        var blink = Hash((int) (time * 2f), 911);
        if (blink > 1f - 0.12f * strength)
            handle.DrawRect(screen, Color.Black.WithAlpha(0.5f * strength));
    }

    /// <summary>
    /// Bugs that are not there: each looms up somewhere on the screen, swells as it rushes at the middle, and is gone.
    /// A red and a blue ghost trail it, like a sight that will not focus
    /// </summary>
    private void DrawPhantoms(DrawingHandleScreen handle, UIBox2 screen, float time, float strength)
    {
        var count = (int) MathF.Round(2 + 6 * strength);
        const float life = 1.8f;

        for (var i = 0; i < count; i++)
        {
            var phase = time / life + i * 0.37f;
            var cycle = (int) MathF.Floor(phase);
            var t = phase - cycle;

            var texture = _phantoms![(int) (Hash(cycle, i) * _phantoms.Length) % _phantoms.Length];
            var start = new Vector2(
                screen.Left + screen.Width * Hash(cycle, i + 13),
                screen.Top + screen.Height * Hash(cycle, i + 29));
            var center = screen.Center + (start - screen.Center) * (1f - 0.6f * t);

            var scale = screen.Height / 260f * (1.5f + 3.5f * t * t) * (0.6f + 0.6f * strength);
            var half = texture.Size * scale / 2f;
            var alpha = MathF.Sin(t * MathF.PI) * (0.25f + 0.45f * strength);

            var shift = new Vector2(6f + 10f * strength, 0f) * MathF.Sin(time * 9f + i);
            Draw(handle, texture, center - shift, half, new Color(1f, 0.15f, 0.15f, alpha * 0.5f));
            Draw(handle, texture, center + shift, half, new Color(0.3f, 0.3f, 1f, alpha * 0.5f));
            Draw(handle, texture, center, half, new Color(0.1f, 0.05f, 0.05f, alpha));
        }
    }

    private static void Draw(DrawingHandleScreen handle, Texture texture, Vector2 center, Vector2 half, Color color)
    {
        handle.DrawTextureRect(texture, new UIBox2(center - half, center + half), color);
    }

    /// <summary>
    /// The edges of sight sink into a dark that pulses like a heartbeat
    /// </summary>
    private static void DrawVignette(DrawingHandleScreen handle, UIBox2 screen, float time, float strength)
    {
        var beat = MathF.Pow(MathF.Max(0f, MathF.Sin(time * 3.2f)), 6f);
        var depth = screen.Height * (0.12f + 0.16f * strength + 0.05f * beat);
        const int bands = 12;

        for (var b = 0; b < bands; b++)
        {
            var inset = depth * b / bands;
            var alpha = (1f - b / (float) bands) * (0.18f + 0.25f * strength) / 2f;
            var color = Color.Black.WithAlpha(alpha);
            var band = depth / bands + 1f;

            handle.DrawRect(new UIBox2(screen.Left, screen.Top + inset, screen.Right, screen.Top + inset + band), color);
            handle.DrawRect(new UIBox2(screen.Left, screen.Bottom - inset - band, screen.Right, screen.Bottom - inset), color);
            handle.DrawRect(new UIBox2(screen.Left + inset, screen.Top, screen.Left + inset + band, screen.Bottom), color);
            handle.DrawRect(new UIBox2(screen.Right - inset - band, screen.Top, screen.Right - inset, screen.Bottom), color);
        }
    }

    /// <summary>
    /// Pairs of red eyes open in the dark at the edges, stare, blink and close
    /// </summary>
    private static void DrawEyes(DrawingHandleScreen handle, UIBox2 screen, float time, float strength)
    {
        var count = (int) MathF.Round(3 + 9 * strength);
        const float life = 2.6f;

        for (var i = 0; i < count; i++)
        {
            var phase = time / life + i * 0.53f;
            var cycle = (int) MathF.Floor(phase);
            var t = phase - cycle;

            // eyes keep to the rim of the screen
            var side = (int) (Hash(cycle, i + 7) * 4f);
            var along = Hash(cycle, i + 41);
            var inward = 0.03f + 0.1f * Hash(cycle, i + 59);
            var position = side switch
            {
                0 => new Vector2(screen.Left + screen.Width * along, screen.Top + screen.Height * inward),
                1 => new Vector2(screen.Left + screen.Width * along, screen.Bottom - screen.Height * inward),
                2 => new Vector2(screen.Left + screen.Width * inward, screen.Top + screen.Height * along),
                _ => new Vector2(screen.Right - screen.Width * inward, screen.Top + screen.Height * along),
            };

            var open = MathF.Sin(t * MathF.PI);
            if (MathF.Abs(t - 0.5f) < 0.04f)
                open = 0f;

            var radius = screen.Height * 0.006f * (1f + Hash(cycle, i + 3));
            var gap = radius * 3f;
            var glow = new Color(1f, 0.1f, 0.05f, 0.25f * open * strength);
            var core = new Color(1f, 0.35f, 0.2f, 0.9f * open);

            foreach (var x in new[] { -gap, gap })
            {
                var eye = position + new Vector2(x, 0f);
                handle.DrawCircle(eye, radius * 3f, glow);
                handle.DrawCircle(eye, radius * open + 0.5f, core);
            }
        }
    }

    /// <summary>
    /// The picture tears: thin bars of noise flicker across the screen
    /// </summary>
    private static void DrawTears(DrawingHandleScreen handle, UIBox2 screen, float time, float strength)
    {
        var frame = (int) (time * 14f);
        var count = (int) MathF.Round(1 + 7 * strength);

        for (var i = 0; i < count; i++)
        {
            if (Hash(frame, i + 101) > 0.35f + 0.4f * strength)
                continue;

            var y = screen.Top + screen.Height * Hash(frame, i + 211);
            var height = 2f + 14f * Hash(frame, i + 307) * strength;
            var color = Hash(frame, i + 401) > 0.5f
                ? new Color(0.9f, 0.1f, 0.2f, 0.25f * strength)
                : new Color(0f, 0f, 0f, 0.45f * strength);

            handle.DrawRect(new UIBox2(screen.Left, y, screen.Right, y + height), color);
        }
    }

    private Texture[] LoadPhantoms()
    {
        var textures = new Texture[Phantoms.Length];
        for (var i = 0; i < Phantoms.Length; i++)
        {
            textures[i] = _sprite.Frame0(new SpriteSpecifier.Rsi(Bugs / Phantoms[i], "alive"));
        }

        return textures;
    }

    private static float Hash(int a, int b)
    {
        var v = MathF.Sin(a * 12.9898f + b * 78.233f) * 43758.547f;
        return v - MathF.Floor(v);
    }
}
