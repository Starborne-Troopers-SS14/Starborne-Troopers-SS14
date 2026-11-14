using System.Numerics;
using Content.Shared._ST14.EnergyFence;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._ST14.ElectricFence;

/// <summary>
/// Client side of the fence: the arc overlay, the blue flicker of a shocked bug's sprite and the glow around it.
/// Startup and shutdown of the shocked component belong to the shared system (speed), so the flicker keeps its own
/// list and puts the colour back once a bug is out of every zone
/// </summary>
public sealed partial class ST14FenceArcSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly EntProtoId GlowPrototype = "ST14FenceArcGlow";
    private static readonly Color FlickerColor = Color.FromHex("#9fe4ff");

    /// <summary>
    /// Flicker changes per second
    /// </summary>
    private const float FlickerRate = 14f;

    /// <summary>
    /// Shocked bugs: their colour before the flicker and the glow hung on them
    /// </summary>
    private readonly Dictionary<EntityUid, (Color Original, EntityUid Glow)> _shocked = new();

    private readonly List<EntityUid> _gone = new();

    public override void Initialize()
    {
        base.Initialize();

        _overlays.AddOverlay(new ST14FenceArcOverlay());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<ST14FenceArcOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var time = (float) _timing.RealTime.TotalSeconds;
        var query = EntityQueryEnumerator<ST14FenceShockedComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var shocked, out var sprite))
        {
            if (!_shocked.TryGetValue(uid, out var entry))
            {
                entry = (sprite.Color, Spawn(GlowPrototype, new EntityCoordinates(uid, Vector2.Zero)));
                _shocked[uid] = entry;
            }

            // Each bug flickers on its own beat, deeper bugs burn bluer
            var tick = (int) (time * FlickerRate + uid.Id * 0.37f);
            var lit = tick % 2 == 0;
            var strength = 0.35f + 0.65f * shocked.Depth;
            var color = lit ? Color.InterpolateBetween(entry.Original, FlickerColor, strength) : entry.Original;
            _sprite.SetColor((uid, sprite), color);
        }

        _gone.Clear();
        foreach (var uid in _shocked.Keys)
        {
            if (!HasComp<ST14FenceShockedComponent>(uid))
                _gone.Add(uid);
        }

        foreach (var uid in _gone)
        {
            var (original, glow) = _shocked[uid];
            _shocked.Remove(uid);

            if (TryComp<SpriteComponent>(uid, out var sprite))
                _sprite.SetColor((uid, sprite), original);

            if (Exists(glow))
                QueueDel(glow);
        }
    }
}
