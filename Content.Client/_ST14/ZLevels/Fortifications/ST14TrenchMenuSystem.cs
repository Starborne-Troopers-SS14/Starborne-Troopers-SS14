/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Rotatable;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Input.Binding;

namespace Content.Client._ST14.ZLevels.Fortifications;

/// <summary>
/// The entrenching tool's build window, opened by using it in hand. Picking a trench, a dug-out or steps puts the ghost
/// on the cursor: R turns it, a click with the tool lays it out there and the soldier starts digging; closing the window
/// puts the ghost away. The placement keeps the normal controls (no editor context), so the click is an ordinary use of
/// the tool and the server reads the mode and the turn off the tool (<see cref="ST14TrenchDiggerComponent"/>).
/// </summary>
public sealed partial class ST14TrenchMenuSystem : EntitySystem
{
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ST14TrenchDiggerSystem _digger = default!;

    private ST14TrenchMenuWindow? _window;
    private EntityUid? _tool;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14OpenTrenchMenuEvent>(OnOpen);

        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.RotateObjectClockwise,
                new PointerInputCmdHandler((_, _, _) => TryRotate(true), outsidePrediction: true), typeof(RotatableSystem))
            .BindBefore(ContentKeyFunctions.RotateObjectCounterclockwise,
                new PointerInputCmdHandler((_, _, _) => TryRotate(false), outsidePrediction: true), typeof(RotatableSystem))
            .Register<ST14TrenchMenuSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<ST14TrenchMenuSystem>();
        _window?.Close();
        _window = null;
    }

    private bool TryRotate(bool clockwise)
    {
        if (_placement.CurrentMode is not ST14TrenchPlacementMode mode ||
            _placement is not PlacementManager { Hijack: ST14TrenchPlacementHijack hijack })
        {
            return false;
        }

        mode.Rotate(clockwise);
        SendMode(hijack.Tool, hijack.Mode);
        return true;
    }

    private void OnOpen(ST14OpenTrenchMenuEvent ev)
    {
        if (_window is { IsOpen: true } && _tool == ev.Tool)
        {
            _window.Close();
            return;
        }

        _tool = ev.Tool;
        if (_window == null)
        {
            _window = new ST14TrenchMenuWindow(EntityManager, _digger);
            _window.ModeSelected += OnModeSelected;
            _window.SizeChanged += OnSizeChanged;
            _window.OnClose += OnClosed;
        }

        if (TryComp<ST14TrenchDiggerComponent>(ev.Tool, out var tool))
            _window.SetTrenchSize(tool.TrenchLength, tool.TrenchWidth, raise: false);

        _window.SetSelected(null);
        _window.OpenCenteredAt(new Vector2(0.5f, 0.85f));
    }

    private void OnModeSelected(ST14DigMode mode)
    {
        if (_tool is not { } tool)
            return;

        if (mode == ST14DigMode.Cable)
        {
            SendMode(tool, mode);
            if (_placement.CurrentMode is ST14TrenchPlacementMode)
                _placement.Clear();

            _window?.Close();
            return;
        }

        _placement.BeginPlacing(new PlacementInformation
            {
                IsTile = false,
                PlacementOption = nameof(ST14TrenchPlacementMode),
                // keep the normal controls: R turns, a click uses the tool
                UseEditorContext = false,
            },
            new ST14TrenchPlacementHijack(tool, mode));

        if (TryComp<ST14TrenchDiggerComponent>(tool, out var digger))
            _placement.Direction = digger.Direction;

        SendMode(tool, mode);
        _window?.SetSelected(mode);
    }

    /// <summary>
    /// ST14: a new size of the trench segment, on the tool at once (the ghost shows it) and to the server
    /// </summary>
    private void OnSizeChanged(int length, int width)
    {
        if (_tool is not { } tool || !TryComp<ST14TrenchDiggerComponent>(tool, out var digger))
            return;

        digger.TrenchLength = length;
        digger.TrenchWidth = width;
        SendMode(tool, digger.Mode);
    }

    private void SendMode(EntityUid tool, ST14DigMode mode)
    {
        var direction = _placement.Direction;

        // predicted, so a click right after R lays the segment out the new way
        var length = 3;
        var width = 1;
        if (TryComp<ST14TrenchDiggerComponent>(tool, out var digger))
        {
            digger.Mode = mode;
            digger.Direction = direction;
            length = digger.TrenchLength;
            width = digger.TrenchWidth;
        }

        RaiseNetworkEvent(new ST14SetDigModeEvent
        {
            Tool = GetNetEntity(tool),
            Mode = mode,
            Direction = direction,
            Length = length,
            Width = width,
        });
    }

    private void OnClosed()
    {
        if (_placement.CurrentMode is ST14TrenchPlacementMode)
            _placement.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_placement is not PlacementManager { Hijack: ST14TrenchPlacementHijack hijack })
            return;

        // the ghost goes with the spade: dropped or put away, building stops
        if (_player.LocalEntity is not { } user || !_hands.IsHolding(user, hijack.Tool))
        {
            _placement.Clear();
            _window?.Close();
        }
    }
}
