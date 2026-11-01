using System.Numerics;
using Content.Shared._ST14.Hive.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Visions of the brain stupor: the screen breathes red and violet, the edges sink into a pulsing dark, bugs that
/// are not there loom up and rush at the middle, and the picture tears in flickering bars
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
    /// How strong the stupor visions of the local player are right now, faded out at the end. Zero without a stupor
    /// </summary>
    public float Strength()
    {
        if (!_entities.TryGetComponent(_player.LocalEntity, out ST14HallucinatingComponent? visions) || !visions.Paralyzed)
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

/// <summary>
/// Vision after the brain bug's scream: the whole picture swims, doubles, sways and flickers, and the eyes keep
/// blinking shut, so the trooper can hardly see what is in front of it
/// </summary>
public sealed partial class ST14ScreamVisionOverlay : Overlay
{
    private const float FadeTime = 2f;
    private static readonly ProtoId<ShaderPrototype> ScreamShader = "ST14HiveScream";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    // made on the first draw: a client without graphics, like the test one, never needs it
    private ShaderInstance? _shader;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public ST14ScreamVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 50;
    }

    private float Strength()
    {
        if (!_entities.TryGetComponent(_player.LocalEntity, out ST14HallucinatingComponent? visions) || visions.Paralyzed)
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
        if (ScreenTexture == null)
            return;

        var strength = Strength();
        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;

        _shader ??= _prototypes.Index(ScreamShader).InstanceUnique();
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("strength", strength);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);

        // the eyes keep blinking shut: a quick dark flash every second or two, a longer one now and then
        var slot = (int) (time / 1.4f);
        var into = time / 1.4f - slot;
        var length = Hash(slot) > 0.7f ? 0.35f : 0.14f;
        if (into < length)
        {
            var close = MathF.Sin(into / length * MathF.PI);
            handle.DrawRect(args.WorldBounds, Color.Black.WithAlpha(0.85f * close * strength));
        }

        // and the light comes and goes in waves
        var dim = 0.5f + 0.5f * MathF.Sin(time * 2.3f) * MathF.Sin(time * 0.7f);
        handle.DrawRect(args.WorldBounds, Color.Black.WithAlpha(0.25f * dim * strength));
    }

    private static float Hash(int i)
    {
        var v = MathF.Sin(i * 12.9898f) * 43758.547f;
        return v - MathF.Floor(v);
    }
}
