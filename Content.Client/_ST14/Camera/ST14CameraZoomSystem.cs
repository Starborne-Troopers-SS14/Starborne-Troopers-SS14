using System.Numerics;
using Content.Client.Movement.Systems;
using Content.Shared.Movement.Components;
using Robust.Client.Player;

namespace Content.Client._ST14.Camera;

/// <summary>
/// Zoom of the camera by the mouse wheel: finer steps than the zoom keys, within the same limits the server keeps
/// (closer down to the minimum zoom, out no further than the default view)
/// </summary>
public sealed partial class ST14CameraZoomSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ContentEyeSystem _eye = default!;

    private const float WheelStep = 1.15f;

    /// <param name="delta">Wheel turn, positive is away from the player: zoom in</param>
    public void Wheel(float delta)
    {
        if (_player.LocalEntity is not { } player || !TryComp<ContentEyeComponent>(player, out var eye))
            return;

        var factor = MathF.Pow(WheelStep, -delta);
        var zoom = Vector2.Clamp(eye.TargetZoom * factor, ContentEyeSystem.MinZoom, eye.MaxZoom);
        if (zoom == eye.TargetZoom)
            return;

        _eye.RequestZoom(player, zoom, false, false, eye);
    }
}
