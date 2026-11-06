using System.Linq;
using Content.Shared._ST14.Medicine;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// The body as its player feels it: the heart thumping in the ears in time with the pulse, the picture throbbing with
/// the pain and swimming with the drugs, a white flash and ringing ears when a limb comes off. And the medicine
/// everyone can see on a body: tourniquets, dressings and splints drawn on the very parts they are on
/// </summary>
public sealed partial class ST14MedicalClientSystem : EntitySystem
{
    private const string LayerPrefix = "st14med_";

    private static readonly SoundSpecifier Heartbeat = new SoundPathSpecifier("/Audio/_ST14/Medicine/heartbeat.wav");
    private static readonly SoundSpecifier Ringing = new SoundPathSpecifier("/Audio/_ST14/Mobs/Bugs/ear_ringing.wav");
    private static readonly ResPath MarksRsi = new("/Textures/_ST14/Mobs/medical_overlays.rsi");

    private static readonly Dictionary<ST14BodyZone, string> Parts = new()
    {
        { ST14BodyZone.Head, "head" },
        { ST14BodyZone.Torso, "torso" },
        { ST14BodyZone.ArmLeft, "l_arm" },
        { ST14BodyZone.ArmRight, "r_arm" },
        { ST14BodyZone.LegLeft, "l_leg" },
        { ST14BodyZone.LegRight, "r_leg" },
    };

    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private ST14MedicalVisionOverlay _vision = default!;
    private ST14MedicHudOverlay _hud = default!;
    private float _phase;
    private int _stumps;

    /// <summary>
    /// The envelope of the current heartbeat of the local player, 1 on the beat and fading to the next one
    /// </summary>
    public float Beat { get; private set; }

    /// <summary>
    /// The white of a shock fading out
    /// </summary>
    public float Flash { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14MedicalShockEvent>(OnShock);
        SubscribeLocalEvent<ST14WoundsComponent, ST14WoundsChangedEvent>(OnWoundsState);

        _vision = new ST14MedicalVisionOverlay();
        _hud = new ST14MedicHudOverlay();
        _overlays.AddOverlay(_vision);
        _overlays.AddOverlay(_hud);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_vision);
        _overlays.RemoveOverlay(_hud);
    }

    private void OnShock(ST14MedicalShockEvent ev)
    {
        Shock(ev.Strength);
    }

    /// <summary>
    /// White before the eyes for a moment, then it fades, and the ears ring
    /// </summary>
    private void Shock(float strength)
    {
        // the state and the event of the same tear can both arrive: one flash and one ringing
        if (Flash > 0.6f)
            return;

        Flash = 1.2f * strength;
        _audio.PlayGlobal(Ringing, Filter.Local(), false, AudioParams.Default.WithVolume(-2f));
    }

    /// <summary>
    /// How loud the heart is in the ears, from 0 to 1
    /// </summary>
    public static float Intensity(ST14WoundsComponent comp)
    {
        var blood = Math.Clamp((0.95f - comp.Blood) / 0.5f, 0f, 1f);
        var racing = Math.Clamp((comp.Pulse - 100f) / 80f, 0f, 1f);
        return MathF.Max(MathF.Max(comp.Pain / 100f, blood), MathF.Max(MathF.Max(comp.Stim, comp.Adrenaline * 0.8f), racing));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        Flash = MathF.Max(0f, Flash - frameTime / 1.6f);

        if (!TryComp<ST14WoundsComponent>(_player.LocalEntity, out var wounds) || _mobState.IsDead(_player.LocalEntity.Value) ||
            wounds.Pulse <= 0 || wounds.Heart == ST14HeartState.Arrest)
        {
            Beat = 0f;
            return;
        }

        _phase += frameTime * wounds.Pulse / 60f;
        if (_phase >= 1f)
        {
            _phase -= MathF.Floor(_phase);
            var intensity = Intensity(wounds);
            if (intensity > 0.15f)
                _audio.PlayGlobal(Heartbeat, Filter.Local(), false, AudioParams.Default.WithVolume(-12f + 14f * intensity));
        }

        // a lub, then a softer dub
        var lub = MathF.Exp(-_phase * 7f);
        var dub = _phase > 0.3f ? 0.6f * MathF.Exp(-(_phase - 0.3f) * 9f) : 0f;
        Beat = MathF.Max(lub, dub);
    }

    #region Marks on the body

    private void OnWoundsState(Entity<ST14WoundsComponent> ent, ref ST14WoundsChangedEvent args)
    {
        // a new stump on the local body: the tear itself, even if its event got lost on the way
        if (ent.Owner == _player.LocalEntity)
        {
            var stumpCount = ent.Comp.Wounds.Count(w => w.Kind == ST14WoundKind.Stump);
            if (stumpCount > _stumps)
                Shock(1f);

            _stumps = stumpCount;
        }

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var stumps = ent.Comp.Wounds.Where(w => w.Kind == ST14WoundKind.Stump).Select(w => w.Zone).ToHashSet();
        var wanted = new HashSet<string>();
        foreach (var (zone, part) in Parts)
        {
            // a stump has nothing left to wear a mark on
            if (stumps.Contains(zone))
                continue;

            var wounds = ent.Comp.Wounds.Where(w => w.Zone == zone).ToList();
            if (ent.Comp.Tourniquets.ContainsKey(zone))
                wanted.Add($"tourniquet_{part}");

            if (wounds.Any(w => w.Bandaged || w.Packed))
                wanted.Add($"bandage_{part}");

            if (wounds.Any(w => w.Kind == ST14WoundKind.Fracture && w.Splinted))
                wanted.Add($"splint_{part}");
        }

        foreach (var part in Parts.Values)
        {
            foreach (var kind in new[] { "tourniquet", "bandage", "splint" })
            {
                var state = $"{kind}_{part}";
                var key = LayerPrefix + state;
                var has = _sprite.LayerMapTryGet((ent.Owner, sprite), key, out _, false);
                if (wanted.Contains(state) == has)
                    continue;

                if (has)
                {
                    _sprite.RemoveLayer((ent.Owner, sprite), key, false);
                    continue;
                }

                var index = _sprite.AddRsiLayer((ent.Owner, sprite), new RSI.StateId(state), MarksRsi);
                _sprite.LayerMapSet((ent.Owner, sprite), key, index);
            }
        }
    }

    #endregion
}
