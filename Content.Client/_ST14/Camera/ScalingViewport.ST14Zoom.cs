using Content.Client._ST14.Camera;
using Robust.Client.UserInterface;

namespace Content.Client.Viewport;

/// <summary>
/// ST14: the mouse wheel over the game zooms the camera of the player in and out
/// </summary>
public sealed partial class ScalingViewport
{
    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        if (args.Handled || args.Delta.Y == 0)
            return;

        IoCManager.Resolve<IEntityManager>().System<ST14CameraZoomSystem>().Wheel(args.Delta.Y);
        args.Handle();
    }
}
