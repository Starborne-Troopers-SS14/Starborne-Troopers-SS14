using Content.Client.ContextMenu.UI;
using Content.Client.Gameplay;
using Content.Client.Interaction;
using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Content.Shared._ST14.Construction.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Rotatable;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14MultitoolSystem : SharedST14MultitoolSystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private InputSystem _input = default!;

    private static readonly Color BuildOutline = Color.FromHex("#ffffff55");
    private static readonly Color DismantleOutline = Color.FromHex("#ff303055");

    private ST14MultitoolMode _heldMode;
    private EntityUid? _heldTool;
    private EntityUid? _heldTarget;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .BindBefore(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUse, outsidePrediction: true),
                typeof(SharedInteractionSystem), typeof(DragDropSystem))
            .BindBefore(EngineKeyFunctions.UseSecondary, new PointerInputCmdHandler(OnUseSecondary, outsidePrediction: true),
                typeof(EntityMenuUIController))
            .BindBefore(ContentKeyFunctions.RotateObjectClockwise, new PointerInputCmdHandler(OnRotateClockwise, outsidePrediction: true),
                typeof(RotatableSystem))
            .BindBefore(ContentKeyFunctions.RotateObjectCounterclockwise, new PointerInputCmdHandler(OnRotateCounterclockwise, outsidePrediction: true),
                typeof(RotatableSystem))
            .BindBefore(EngineKeyFunctions.EditorPlaceObject, new PointerInputCmdHandler(OnPlaceObject, outsidePrediction: true),
                typeof(PlacementManager))
            .Register<ST14MultitoolSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<ST14MultitoolSystem>();
    }

    public void PlaceBlueprint(ST14EngineerBuildablePrototype buildable, EntityCoordinates coords, Direction direction)
    {
        RaiseNetworkEvent(new ST14PlaceBlueprintEvent
        {
            Buildable = buildable.ID,
            Coordinates = GetNetCoordinates(coords),
            Direction = direction,
        });
    }

    public List<ST14BuildMenuEntry> GetBuildEntries()
    {
        var entries = new List<ST14BuildMenuEntry>();

        foreach (var buildable in ProtoMan.EnumeratePrototypes<ST14EngineerBuildablePrototype>())
        {
            var name = buildable.Name is { } loc
                ? Loc.GetString(loc)
                : ProtoMan.TryIndex(buildable.Result, out var result) ? result.Name : buildable.ID;

            entries.Add(new ST14BuildMenuEntry(buildable, name));
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        return entries;
    }

    public FixedPoint2? GetLocalArmium()
    {
        if (_player.LocalEntity is not { } user || !TryGetHeldMultitool(user, out var tool))
            return null;

        if (!TryGetCapsule(user, out var capsule) || !Capsules.IsCompatible(capsule, tool.Comp.Resource))
            return FixedPoint2.Zero;

        return capsule.Comp.Amount;
    }

    public Color? GetOutlineColor(EntityUid? hovered)
    {
        if (hovered is not { } uid || !IsValidTarget(uid, ST14MultitoolMode.Dismantle) || !TryGetLocalMultitool(out _))
            return null;

        return _heldMode == ST14MultitoolMode.Dismantle && _heldTarget == uid ? DismantleOutline : BuildOutline;
    }

    private bool OnUse(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        return IsValidTarget(uid, ST14MultitoolMode.Build) && StartHolding(uid, ST14MultitoolMode.Build);
    }

    private bool OnUseSecondary(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        return IsValidTarget(uid, ST14MultitoolMode.Dismantle) && StartHolding(uid, ST14MultitoolMode.Dismantle);
    }

    private bool OnPlaceObject(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (_placement.CurrentMode is ST14AlignBlueprint mode &&
            mode.GetPlacementError() is { } error &&
            _player.LocalEntity is { } user)
        {
            Popup.PopupCursor(Loc.GetString(error), user);
        }

        return false;
    }

    private bool OnRotateClockwise(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (_placement.CurrentMode is not ST14AlignBlueprint mode)
            return false;

        mode.Rotate();
        return true;
    }

    private bool OnRotateCounterclockwise(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (_placement.CurrentMode is not ST14AlignBlueprint mode)
            return false;

        for (var i = 0; i < 3; i++)
        {
            mode.Rotate();
        }

        return true;
    }

    private bool IsValidTarget(EntityUid uid, ST14MultitoolMode mode)
    {
        return HasComp<ST14BlueprintComponent>(uid) ||
               mode == ST14MultitoolMode.Dismantle && HasComp<ST14EngineerStructureComponent>(uid);
    }

    private bool StartHolding(EntityUid target, ST14MultitoolMode mode)
    {
        if (!TryGetLocalMultitool(out var tool))
            return false;

        _heldMode = mode;
        _heldTool = tool;
        _heldTarget = target;

        RaisePredictiveEvent(new ST14StartBuildingEvent
        {
            Tool = GetNetEntity(tool),
            Target = GetNetEntity(target),
            Mode = mode,
        });
        return true;
    }

    private void StopHolding(EntityUid tool)
    {
        _heldMode = ST14MultitoolMode.None;
        _heldTool = null;
        _heldTarget = null;

        if (TryGetNetEntity(tool, out var netTool))
            RaisePredictiveEvent(new ST14StopBuildingEvent { Tool = netTool.Value });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_heldTool is not { } heldTool || !Timing.IsFirstTimePredicted)
            return;

        var key = _heldMode == ST14MultitoolMode.Dismantle ? EngineKeyFunctions.UseSecondary : EngineKeyFunctions.Use;
        if (_input.CmdStates.GetState(key) != BoundKeyState.Down ||
            !TryGetLocalMultitool(out var tool) || tool.Owner != heldTool)
        {
            StopHolding(heldTool);
            return;
        }

        var hovered = GetHoveredEntity();
        if (hovered == _heldTarget)
            return;

        if (hovered is { } next && IsValidTarget(next, _heldMode))
            StartHolding(next, _heldMode);
        else
            StopHolding(heldTool);
    }

    private EntityUid? GetHoveredEntity()
    {
        if (_state.CurrentState is not GameplayStateBase gameplay)
            return null;

        return gameplay.GetClickedEntity(_eye.PixelToMap(_inputManager.MouseScreenPosition));
    }

    private bool TryGetLocalMultitool(out Entity<ST14MultitoolComponent> tool)
    {
        tool = default;
        return _player.LocalEntity is { } user && TryGetHeldMultitool(user, out tool);
    }
}

public sealed record ST14BuildMenuEntry(ST14EngineerBuildablePrototype Buildable, string Name);
