using Content.Shared._ST14.Medicine;
using Content.Shared.Mobs.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Medicine;

/// <summary>
/// The picture of a wounded body: pain throbbing red at the edges with the heart, the colours draining with the blood,
/// morphine warm and soft, a stimulant cold and sharp, the crash heavy-eyed, ketamine floating, a shock white.
/// Every effect eases in and out instead of popping
/// </summary>
public sealed partial class ST14MedicalVisionOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> Shader = "ST14MedicalVision";

    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private ShaderInstance? _shader;

    private float _pain;
    private float _bloodLoss;
    private float _morphine;
    private float _stim;
    private float _crash;
    private float _ketamine;
    private float _fever;
    private float _toxin;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public ST14MedicalVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 40;
    }

    private static float Ease(float current, float target, float frameTime)
    {
        return current + (target - current) * MathF.Min(1f, frameTime * 2.5f);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var dt = args.DeltaSeconds;
        float pain = 0, blood = 0, morphine = 0, stim = 0, crash = 0, ketamine = 0, fever = 0, toxin = 0;
        if (_entities.TryGetComponent(_player.LocalEntity, out ST14WoundsComponent? wounds) &&
            !_entities.System<MobStateSystem>().IsDead(_player.LocalEntity.Value))
        {
            pain = Math.Clamp((wounds.Pain - 15f) / 85f, 0f, 1f);
            blood = Math.Clamp((0.92f - wounds.Blood) / 0.5f, 0f, 1f);
            morphine = wounds.Sedation;
            stim = MathF.Max(wounds.Stim, wounds.Adrenaline * 0.5f);
            crash = wounds.Crash;
            ketamine = wounds.Dissociation;
            fever = wounds.Fever;
            toxin = wounds.Toxin;
        }

        _pain = Ease(_pain, pain, dt);
        _bloodLoss = Ease(_bloodLoss, blood, dt);
        _morphine = Ease(_morphine, morphine, dt);
        _stim = Ease(_stim, stim, dt);
        _crash = Ease(_crash, crash, dt);
        _ketamine = Ease(_ketamine, ketamine, dt);
        _fever = Ease(_fever, fever, dt);
        _toxin = Ease(_toxin, toxin, dt);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var flash = _entities.System<ST14MedicalClientSystem>().Flash;
        return _pain + _bloodLoss + _morphine + _stim + _crash + _ketamine + _fever + _toxin + flash > 0.01f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var client = _entities.System<ST14MedicalClientSystem>();
        _shader ??= _prototypes.Index(Shader).InstanceUnique();
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("pain", _pain);
        _shader.SetParameter("beat", client.Beat);
        _shader.SetParameter("blood_loss", _bloodLoss);
        _shader.SetParameter("morphine", _morphine);
        _shader.SetParameter("stim", _stim);
        _shader.SetParameter("crash", _crash);
        _shader.SetParameter("ketamine", _ketamine);
        _shader.SetParameter("fever", _fever);
        _shader.SetParameter("toxin", _toxin);
        _shader.SetParameter("flash", client.Flash);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
