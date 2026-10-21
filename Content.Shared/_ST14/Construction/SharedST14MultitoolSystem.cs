using System.Diagnostics.CodeAnalysis;
using Content.Shared._ST14.Construction.Components;
using Content.Shared._ST14.ST14Resources.Components;
using Content.Shared._ST14.ST14Resources.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Shared._ST14.Construction.EntitySystems;

public abstract partial class SharedST14MultitoolSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected SharedPopupSystem Popup = default!;
    [Dependency] protected ST14ResourceCapsuleSystem Capsules = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<ST14StartBuildingEvent>(OnStartBuilding);
        SubscribeAllEvent<ST14StopBuildingEvent>(OnStopBuilding);
        SubscribeLocalEvent<ST14MultitoolComponent, ExaminedEvent>(OnExamined);
    }

    private void OnStartBuilding(ST14StartBuildingEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryGetEntity(ev.Tool, out var toolUid) ||
            !TryComp<ST14MultitoolComponent>(toolUid, out var tool) ||
            !TryGetEntity(ev.Target, out var target))
            return;

        StartWorking((toolUid.Value, tool), user, target.Value, ev.Mode);
    }

    private void OnStopBuilding(ST14StopBuildingEvent ev, EntitySessionEventArgs args)
    {
        if (TryGetEntity(ev.Tool, out var toolUid) &&
            TryComp<ST14MultitoolComponent>(toolUid, out var tool) &&
            tool.User == args.SenderSession.AttachedEntity)
            Stop((toolUid.Value, tool));
    }

    private void OnExamined(Entity<ST14MultitoolComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetCapsule(args.Examiner, out var capsule))
        {
            args.PushMarkup(Loc.GetString("st14-multitool-examine-no-capsule"));
            return;
        }

        args.PushMarkup(Loc.GetString("st14-multitool-examine-capsule",
            ("amount", capsule.Comp.Amount.Int()),
            ("capacity", capsule.Comp.Capacity.Int())));
    }

    private void StartWorking(Entity<ST14MultitoolComponent> tool, EntityUid user, EntityUid target, ST14MultitoolMode mode)
    {
        Stop(tool);

        if (mode == ST14MultitoolMode.None)
            return;

        if (GetUseError(tool, user, mode) is { } error)
        {
            Popup.PopupEntity(Loc.GetString(error), user, user);
            return;
        }

        if (!InReach(user, target, tool.Comp.Range))
        {
            Popup.PopupEntity(Loc.GetString("st14-multitool-out-of-range"), user, user);
            return;
        }

        if (!TryComp<ST14BlueprintComponent>(target, out var blueprint))
        {
            if (mode != ST14MultitoolMode.Dismantle ||
                !TryComp<ST14EngineerStructureComponent>(target, out var structure) ||
                !TryTurnIntoBlueprint((target, structure), out var created))
                return;

            target = created.Owner;
            blueprint = created.Comp;
        }

        Settle((target, blueprint));

        tool.Comp.Target = target;
        tool.Comp.Mode = mode;
        tool.Comp.User = user;
        Dirty(tool);

        UpdateSpeed((target, blueprint));
    }

    public float GetProgress(ST14BlueprintComponent blueprint)
    {
        var elapsed = (float) (Timing.CurTime - blueprint.LastUpdate).TotalSeconds;
        return Math.Clamp(blueprint.Progress + blueprint.Speed * elapsed, 0f, 1f);
    }

    public string? GetPlacementError(EntityUid user, ST14EngineerBuildablePrototype buildable, EntityCoordinates coords, Direction direction, out EntityCoordinates spot)
    {
        spot = coords;

        if (!TryGetHeldMultitool(user, out var tool))
            return "st14-multitool-required";

        if (GetUseError(tool, user, ST14MultitoolMode.None) is { } useError)
            return useError;

        if (!_transform.InRange(Transform(user).Coordinates, coords, tool.Comp.Range))
            return "st14-multitool-out-of-range";

        foreach (var condition in buildable.Conditions)
        {
            if (!condition.Condition(user, coords, direction))
                return condition.GenerateGuideEntry()?.Localization.Id ?? "st14-blueprint-cant-place";
        }

        if (_transform.GetGrid(coords) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return "st14-blueprint-cant-place";

        var tile = _map.TileIndicesFor(gridUid, grid, coords);
        spot = _map.GridTileToLocal(gridUid, grid, tile);

        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<ST14BlueprintComponent>(uid) || HasComp<ST14EngineerStructureComponent>(uid))
                return "st14-blueprint-spot-taken";
        }

        return null;
    }

    protected bool TryGetBuildable(ST14BlueprintComponent blueprint, [NotNullWhen(true)] out ST14EngineerBuildablePrototype? buildable)
    {
        buildable = null;
        if (blueprint.Buildable is not { } id)
            return false;

        return ProtoMan.TryIndex(id, out buildable);
    }

    protected bool TryGetHeldMultitool(EntityUid user, out Entity<ST14MultitoolComponent> tool)
    {
        tool = default;
        if (_hands.GetActiveItem(user) is not { } held || !TryComp<ST14MultitoolComponent>(held, out var comp))
            return false;

        tool = (held, comp);
        return true;
    }

    protected bool TryGetCapsule(EntityUid user, out Entity<ST14ResourceCapsuleComponent> capsule)
    {
        capsule = default;
        if (!_inventory.TryGetSlotEntity(user, ST14MultitoolComponent.CapsuleSlot, out var item) ||
            !TryComp<ST14ResourceCapsuleComponent>(item, out var comp))
            return false;

        capsule = (item.Value, comp);
        return true;
    }

    protected string? GetUseError(Entity<ST14MultitoolComponent> tool, EntityUid user, ST14MultitoolMode mode)
    {
        if (_hands.GetActiveItem(user) != tool.Owner)
            return "st14-multitool-not-in-hand";

        if (tool.Comp.RequiresSuit &&
            (!_inventory.TryGetSlotEntity(user, ST14MultitoolComponent.SuitSlot, out var suit) || !HasComp<ST14EngineerSuitComponent>(suit)))
            return "st14-multitool-no-suit";

        if (mode == ST14MultitoolMode.None)
            return null;

        if (!TryGetCapsule(user, out var capsule))
            return "st14-multitool-no-capsule";

        if (!Capsules.IsCompatible(capsule, tool.Comp.Resource))
            return "st14-multitool-wrong-capsule";

        return mode == ST14MultitoolMode.Build && Capsules.IsEmpty(capsule) ? "st14-multitool-not-enough" : null;
    }

    protected bool InReach(EntityUid user, EntityUid target, float range)
    {
        return _transform.InRange(Transform(user).Coordinates, Transform(target).Coordinates, range);
    }

    protected void Stop(Entity<ST14MultitoolComponent> tool, string? reason = null, bool settle = true)
    {
        if (tool.Comp.Target is not { } target)
            return;

        TryComp<ST14BlueprintComponent>(target, out var blueprint);
        if (settle && blueprint != null)
            Settle((target, blueprint));

        if (reason != null && tool.Comp.User is { } user)
            Popup.PopupEntity(Loc.GetString(reason), user, user);

        tool.Comp.Target = null;
        tool.Comp.Mode = ST14MultitoolMode.None;
        tool.Comp.User = null;
        Dirty(tool);

        if (blueprint != null)
            UpdateSpeed((target, blueprint));
    }

    protected virtual void Settle(Entity<ST14BlueprintComponent> blueprint)
    {
        blueprint.Comp.Progress = GetProgress(blueprint.Comp);
        blueprint.Comp.LastUpdate = Timing.CurTime;
        Dirty(blueprint);
    }

    protected virtual bool TryTurnIntoBlueprint(Entity<ST14EngineerStructureComponent> structure, out Entity<ST14BlueprintComponent> blueprint)
    {
        blueprint = default;
        return false;
    }

    protected void UpdateSpeed(Entity<ST14BlueprintComponent> blueprint)
    {
        var speed = 0f;

        if (TryGetBuildable(blueprint.Comp, out var buildable))
        {
            var tools = EntityQueryEnumerator<ST14MultitoolComponent>();
            while (tools.MoveNext(out _, out var tool))
            {
                if (tool.Target == blueprint.Owner)
                    speed += GetSpeed(tool, buildable);
            }
        }

        if (blueprint.Comp.Speed == speed)
            return;

        blueprint.Comp.Speed = speed;
        Dirty(blueprint);
    }

    protected static float GetSpeed(ST14MultitoolComponent tool, ST14EngineerBuildablePrototype buildable)
    {
        var time = MathF.Max(buildable.BuildTime, 0.01f);

        if (tool.Mode == ST14MultitoolMode.Build)
            return tool.BuildSpeed / time;

        if (tool.Mode == ST14MultitoolMode.Dismantle)
            return -tool.DismantleSpeed / time;

        return 0f;
    }
}
