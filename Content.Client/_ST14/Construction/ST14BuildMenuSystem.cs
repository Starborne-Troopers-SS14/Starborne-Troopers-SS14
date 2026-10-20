using System.Numerics;
using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Input;
using Robust.Client.Placement;
using Robust.Shared.Enums;
using Robust.Shared.Input.Binding;
using Robust.Shared.Timing;

namespace Content.Client._ST14.Construction;

public sealed partial class ST14BuildMenuSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private ST14MultitoolSystem _multitool = default!;

    private static readonly TimeSpan RefreshDelay = TimeSpan.FromSeconds(0.25);

    private ST14BuildMenuWindow? _window;
    private string? _selected;
    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ST14KeyFunctions.OpenEngineerBuildMenu, InputCmdHandler.FromDelegate(_ => ToggleMenu()))
            .Register<ST14BuildMenuSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<ST14BuildMenuSystem>();
        _window?.Close();
        _window = null;
    }

    private void ToggleMenu()
    {
        if (_window is { IsOpen: true })
        {
            _window.Close();
            return;
        }

        if (_window == null)
        {
            _window = new ST14BuildMenuWindow();
            _window.BuildableSelected += OnBuildableSelected;
            _window.OnClose += OnClosed;
        }

        _window.SetEntries(EntityManager, _multitool.GetBuildEntries());
        Refresh();
        _window.OpenCenteredAt(new Vector2(0.5f, 0.85f));
    }

    private void OnBuildableSelected(ST14EngineerBuildablePrototype buildable)
    {
        _selected = buildable.ID;

        _placement.BeginPlacing(new PlacementInformation
            {
                IsTile = false,
                PlacementOption = nameof(ST14AlignBlueprint),
            },
            new ST14BlueprintPlacementHijack(buildable));

        Refresh();
    }

    private void OnClosed()
    {
        if (_placement.CurrentMode is ST14AlignBlueprint)
            _placement.Clear();

        _selected = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_window is not { IsOpen: true } || _timing.RealTime < _nextRefresh)
            return;

        if (_selected != null && _placement.CurrentMode is not ST14AlignBlueprint)
            _selected = null;

        Refresh();
    }

    private void Refresh()
    {
        _nextRefresh = _timing.RealTime + RefreshDelay;
        _window?.UpdateState(_multitool.GetLocalArmium(), _selected);
    }
}
