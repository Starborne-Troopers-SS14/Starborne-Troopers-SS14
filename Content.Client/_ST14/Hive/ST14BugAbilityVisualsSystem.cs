using Content.Client.Camera;
using Content.Client.DamageState;
using Content.Client.Graphics;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Mobs.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Looks of the caste abilities. A larva riding a bug and a trooper in slime are seen by all, the rest only by bugs: green motes and a pale outline on resonating warriors, a green glowing shield on the bugs of a
/// living barricade, a red outline and sparks on a tiger in its last rush, and for the bugs that smell blood the red
/// trails and the reddened bodies of the wounded troopers
/// </summary>
public sealed partial class ST14BugAbilityVisualsSystem : EntitySystem
{
    private const string OutlineId = "st14-ability-outline";
    private static readonly ProtoId<ShaderPrototype> OutlineShader = "SelectionOutlineInrange";

    private static readonly Color RageOutline = new(1f, 0.1f, 0.05f);
    private static readonly Color ShieldOutline = new(0.4f, 1f, 0.3f);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedST14HiveSystem _hive = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private CameraRecoilSystem _recoil = default!;

    private ST14ResonanceOverlay _resonance = default!;
    private ST14RageOverlay _rage = default!;
    private ST14BloodScentOverlay _scent = default!;
    private ST14GripStruggleOverlay _struggle = default!;
    private ST14HallucinationOverlay _hallucination = default!;
    private ST14ScreamVisionOverlay _screamVision = default!;

    // made on the first frame: a client without graphics, like the test one, never needs them
    private ShaderInstance? _rageShader;
    private ShaderInstance? _shieldShader;
    private ShaderInstance? _resonanceShader;

    // entities this system has painted, so their looks can be put back
    private readonly HashSet<EntityUid> _outlined = new();
    private readonly HashSet<EntityUid> _tinted = new();
    private readonly HashSet<EntityUid> _outlinedNow = new();
    private readonly HashSet<EntityUid> _tintedNow = new();
    private readonly HashSet<EntityUid> _riders = new();
    private readonly HashSet<EntityUid> _ridersNow = new();
    private readonly HashSet<EntityUid> _mounts = new();
    private readonly HashSet<EntityUid> _mountsNow = new();
    private TimeSpan _nextShake;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14BloodScentEvent>(OnBloodScent);

        _resonance = new ST14ResonanceOverlay();
        _rage = new ST14RageOverlay();
        _scent = new ST14BloodScentOverlay();
        _struggle = new ST14GripStruggleOverlay();
        _hallucination = new ST14HallucinationOverlay();
        _screamVision = new ST14ScreamVisionOverlay();

        _overlays.AddOverlay(_resonance);
        _overlays.AddOverlay(_rage);
        _overlays.AddOverlay(_scent);
        _overlays.AddOverlay(_struggle);
        _overlays.AddOverlay(_hallucination);
        _overlays.AddOverlay(_screamVision);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay(_resonance);
        _overlays.RemoveOverlay(_rage);
        _overlays.RemoveOverlay(_scent);
        _overlays.RemoveOverlay(_struggle);
        _overlays.RemoveOverlay(_hallucination);
        _overlays.RemoveOverlay(_screamVision);
    }

    private void OnBloodScent(ST14BloodScentEvent args)
    {
        _scent.SetTrails(args.Trails, _timing.RealTime);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var pulse = 0.5f + 0.5f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 6f);

        _rageShader ??= MakeOutline();
        _shieldShader ??= MakeOutline();
        _resonanceShader ??= MakeOutline(1f);
        _rageShader.SetParameter("outline_color", RageOutline.WithAlpha(0.6f + 0.4f * pulse));
        _shieldShader.SetParameter("outline_color", ShieldOutline.WithAlpha(0.55f + 0.35f * pulse));
        _resonanceShader.SetParameter("outline_color", ShieldOutline.WithAlpha(0.25f + 0.2f * pulse));

        _outlinedNow.Clear();
        _tintedNow.Clear();
        _ridersNow.Clear();
        _mountsNow.Clear();

        // seen by everyone: a larva riding is drawn by its mount's sprite, the larva itself is hidden
        var riders = EntityQueryEnumerator<ST14RidingComponent, SpriteComponent>();
        while (riders.MoveNext(out var uid, out _, out var sprite))
        {
            _ridersNow.Add(uid);
            if (_riders.Add(uid))
                _sprite.SetVisible((uid, sprite), false);
        }

        var mounts = EntityQueryEnumerator<ST14CarryingRiderComponent, SpriteComponent>();
        while (mounts.MoveNext(out var uid, out _, out var sprite))
        {
            _mountsNow.Add(uid);
            _mounts.Add(uid);
            SwapLarvaState((uid, sprite), true);
        }

        // a trooper frozen in the brain bug's stupor: the camera shudders with it
        if (_player.LocalEntity is { } me &&
            TryComp<ST14HallucinatingComponent>(me, out var visions) &&
            visions.Paralyzed &&
            _timing.RealTime >= _nextShake)
        {
            _nextShake = _timing.RealTime + TimeSpan.FromSeconds(0.12);
            _recoil.KickCamera(me, _random.NextVector2(0.25f));
        }

        // the glow of the hive is for bug eyes only, the troopers see plain bugs
        var bugEyes = _player.LocalEntity is { } local && _hive.IsHive(local);
        _resonance.Visible = bugEyes;
        _rage.Visible = bugEyes;

        if (!bugEyes)
        {
            Restore();
            return;
        }

        var rush = EntityQueryEnumerator<ST14LastRushComponent, SpriteComponent>();
        while (rush.MoveNext(out var uid, out var comp, out var sprite))
        {
            if (!comp.Active || _mobState.IsDead(uid))
                continue;

            Outline((uid, sprite), _rageShader);
            Tint((uid, sprite), Color.InterpolateBetween(Color.White, new Color(1f, 0.45f, 0.4f), pulse));
        }

        // a warrior at full resonance glows faintly, a wall shield outshines it
        var resonance = EntityQueryEnumerator<ST14SwarmResonanceComponent, SpriteComponent>();
        while (resonance.MoveNext(out var uid, out var comp, out var sprite))
        {
            if (comp.Allies >= comp.MaxAllies && !_mobState.IsDead(uid) && !HasComp<ST14InBarricadeComponent>(uid))
                Outline((uid, sprite), _resonanceShader);
        }

        var wall = EntityQueryEnumerator<ST14InBarricadeComponent, SpriteComponent>();
        while (wall.MoveNext(out var uid, out _, out var sprite))
        {
            Outline((uid, sprite), _shieldShader);
            Tint((uid, sprite), Color.InterpolateBetween(Color.White, new Color(0.7f, 1f, 0.6f), 0.5f * pulse));
        }

        // the wounded troopers a tiger smells glow red to it
        foreach (var net in _scent.Troopers())
        {
            if (TryGetEntity(net, out var trooper) && TryComp<SpriteComponent>(trooper, out var sprite))
                Tint((trooper.Value, sprite), Color.InterpolateBetween(new Color(1f, 0.55f, 0.55f), new Color(1f, 0.25f, 0.25f), pulse));
        }

        Restore();
    }

    private void Outline(Entity<SpriteComponent> ent, ShaderInstance shader)
    {
        _outlinedNow.Add(ent);
        if (_outlined.Add(ent) || !_sprite.TryGetPostShader(ent.AsNullable(), OutlineId, out var entry) || entry.Shader != shader)
        {
            _sprite.SetPostShader(ent.AsNullable(), new SpriteComponent.PostShaderArgs(OutlineId, shader)
            {
                After = ContentPostShaderIds.AfterBaseEffects,
            });
        }
    }

    private void Tint(Entity<SpriteComponent> ent, Color color)
    {
        _tintedNow.Add(ent);
        _tinted.Add(ent);
        _sprite.SetColor(ent.AsNullable(), color);
    }

    /// <summary>
    /// Puts back the looks of everything that lost its effect this frame
    /// </summary>
    private void Restore()
    {
        _mounts.RemoveWhere(uid =>
        {
            if (_mountsNow.Contains(uid))
                return false;

            if (TryComp<SpriteComponent>(uid, out var sprite))
                SwapLarvaState((uid, sprite), false);

            return true;
        });

        _riders.RemoveWhere(uid =>
        {
            if (_ridersNow.Contains(uid))
                return false;

            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetVisible((uid, sprite), true);

            return true;
        });

        _outlined.RemoveWhere(uid =>
        {
            if (_outlinedNow.Contains(uid))
                return false;

            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.RemovePostShader((uid, sprite), OutlineId);

            return true;
        });

        _tinted.RemoveWhere(uid =>
        {
            if (_tintedNow.Contains(uid))
                return false;

            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetColor((uid, sprite), Color.White);

            return true;
        });
    }

    /// <summary>
    /// Shows or hides the larva drawn on a mount's back: the base sprite and the brain bug's proboscis layer switch to
    /// their "_larva" states. A dead bug keeps its corpse sprite
    /// </summary>
    private void SwapLarvaState(Entity<SpriteComponent> ent, bool larva)
    {
        SwapState(ent, DamageStateVisualLayers.Base, larva);
        SwapState(ent, "sucking", larva);
    }

    private void SwapState(Entity<SpriteComponent> ent, object key, bool larva)
    {
        var found = key is Enum e
            ? _sprite.LayerMapTryGet(ent.AsNullable(), e, out var index, false)
            : _sprite.LayerMapTryGet(ent.AsNullable(), (string) key, out index, false);
        if (!found)
            return;

        var state = _sprite.LayerGetRsiState(ent.AsNullable(), index).Name;
        if (state == null)
            return;

        if (larva && (state == "alive" || state == "sucking"))
            _sprite.LayerSetRsiState(ent.AsNullable(), index, state + "_larva");
        else if (!larva && state.EndsWith("_larva"))
            _sprite.LayerSetRsiState(ent.AsNullable(), index, state[..^"_larva".Length]);
    }

    private ShaderInstance MakeOutline(float width = 2f)
    {
        var shader = _prototypes.Index(OutlineShader).InstanceUnique();
        shader.SetParameter("outline_width", width);
        shader.SetParameter("outline_fullbright", true);
        return shader;
    }
}
