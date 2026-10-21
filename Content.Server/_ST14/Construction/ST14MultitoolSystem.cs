using Content.Shared._ST14.Construction;
using Content.Shared._ST14.Construction.Components;
using Content.Shared._ST14.Construction.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._ST14.Construction;

public sealed partial class ST14MultitoolSystem : SharedST14MultitoolSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    private const float SettleInterval = 0.2f;

    private float _accumulator;
    private readonly List<Entity<ST14BlueprintComponent>> _inProgress = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ST14PlaceBlueprintEvent>(OnPlaceBlueprint);
    }

    private void OnPlaceBlueprint(ST14PlaceBlueprintEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !ProtoMan.TryIndex(ev.Buildable, out var buildable))
            return;

        if (GetPlacementError(user, buildable, GetCoordinates(ev.Coordinates), ev.Direction, out var spot) is { } error)
        {
            Popup.PopupEntity(Loc.GetString(error), user, user);
            return;
        }

        var rotation = buildable.CanRotate ? ev.Direction.ToAngle() : Angle.Zero;
        var uid = SpawnAttachedTo(buildable.Blueprint, spot, rotation: rotation);
        var blueprint = Comp<ST14BlueprintComponent>(uid);
        blueprint.Buildable = buildable.ID;
        blueprint.LastUpdate = Timing.CurTime;
        Dirty(uid, blueprint);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < SettleInterval)
            return;

        _accumulator = 0f;

        var tools = EntityQueryEnumerator<ST14MultitoolComponent>();
        while (tools.MoveNext(out var uid, out var tool))
        {
            if (tool.Target is not { } target || tool.User is not { } user)
                continue;

            if (GetUseError((uid, tool), user, tool.Mode) is { } error)
                Stop((uid, tool), error);
            else if (!Exists(target) || !InReach(user, target, tool.Range))
                Stop((uid, tool), "st14-multitool-out-of-range");
        }

        _inProgress.Clear();
        var blueprints = EntityQueryEnumerator<ST14BlueprintComponent>();
        while (blueprints.MoveNext(out var uid, out var blueprint))
        {
            if (blueprint.Speed != 0f)
                _inProgress.Add((uid, blueprint));
        }

        foreach (var blueprint in _inProgress)
        {
            var dismantling = blueprint.Comp.Speed < 0f;
            Settle(blueprint);

            if (blueprint.Comp.Progress >= 1f)
                Complete(blueprint);
            else if (dismantling && blueprint.Comp.Progress <= 0f)
                DeleteBlueprint(blueprint);
        }
    }

    protected override void Settle(Entity<ST14BlueprintComponent> blueprint)
    {
        var now = Timing.CurTime;
        var dt = (float) (now - blueprint.Comp.LastUpdate).TotalSeconds;
        var progress = blueprint.Comp.Progress;

        if (dt > 0f && TryGetBuildable(blueprint.Comp, out var buildable))
        {
            var tools = EntityQueryEnumerator<ST14MultitoolComponent>();
            while (tools.MoveNext(out var uid, out var tool))
            {
                if (tool.Target == blueprint.Owner)
                    progress += DoWork((uid, tool), blueprint.Comp, buildable, dt, progress);
            }
        }

        blueprint.Comp.Progress = Math.Clamp(progress, 0f, 1f);
        blueprint.Comp.LastUpdate = now;
        Dirty(blueprint);
    }

    private float DoWork(Entity<ST14MultitoolComponent> tool, ST14BlueprintComponent blueprint, ST14EngineerBuildablePrototype buildable, float dt, float progress)
    {
        var work = Math.Clamp(GetSpeed(tool.Comp, buildable) * dt, -progress, 1f - progress);
        if (work == 0f || buildable.Cost <= FixedPoint2.Zero)
            return work;

        if (tool.Comp.User is not { } user || !TryGetCapsule(user, out var capsule))
        {
            Stop(tool, "st14-multitool-no-capsule", settle: false);
            return 0f;
        }

        var before = PriceAt(buildable, progress);
        var after = PriceAt(buildable, progress + work);

        if (work < 0f)
        {
            var refund = FixedPoint2.Min(before - after, blueprint.Paid);
            blueprint.Paid -= refund;
            Capsules.TryAdd(capsule, tool.Comp.Resource, refund * tool.Comp.RefundRatio);
            return work;
        }

        var amount = after - before;
        var paid = Capsules.TryRemove(capsule, tool.Comp.Resource, amount);
        blueprint.Paid += paid;

        if (paid >= amount)
            return work;

        Stop(tool, "st14-multitool-not-enough", settle: false);
        return work * paid.Float() / amount.Float();
    }

    private static FixedPoint2 PriceAt(ST14EngineerBuildablePrototype buildable, float progress)
    {
        return FixedPoint2.New(buildable.Cost.Float() * progress);
    }

    private void Complete(Entity<ST14BlueprintComponent> blueprint)
    {
        if (!TryGetBuildable(blueprint.Comp, out var buildable))
            return;

        var xform = Transform(blueprint);
        var structure = SpawnAttachedTo(buildable.Result, xform.Coordinates, rotation: xform.LocalRotation);
        var built = EnsureComp<ST14EngineerStructureComponent>(structure);
        built.Buildable = buildable.ID;
        built.Paid = blueprint.Comp.Paid;
        Dirty(structure, built);

        _audio.PlayPvs(blueprint.Comp.CompleteSound, structure);
        DeleteBlueprint(blueprint);
    }

    private void DeleteBlueprint(Entity<ST14BlueprintComponent> blueprint)
    {
        var tools = EntityQueryEnumerator<ST14MultitoolComponent>();
        while (tools.MoveNext(out var uid, out var tool))
        {
            if (tool.Target == blueprint.Owner)
                Stop((uid, tool), settle: false);
        }

        QueueDel(blueprint);
    }

    protected override bool TryTurnIntoBlueprint(Entity<ST14EngineerStructureComponent> structure, out Entity<ST14BlueprintComponent> blueprint)
    {
        blueprint = default;
        if (structure.Comp.Buildable is not { } id || !ProtoMan.TryIndex(id, out var buildable))
            return false;

        var xform = Transform(structure);

        foreach (var container in _container.GetAllContainers(structure))
        {
            _container.EmptyContainer(container, true, xform.Coordinates);
        }

        var uid = SpawnAttachedTo(buildable.Blueprint, xform.Coordinates, rotation: xform.LocalRotation);
        var comp = Comp<ST14BlueprintComponent>(uid);
        comp.Buildable = id;
        comp.Progress = 1f;
        comp.Paid = structure.Comp.Paid;
        comp.LastUpdate = Timing.CurTime;
        Dirty(uid, comp);

        QueueDel(structure);
        blueprint = (uid, comp);
        return true;
    }
}
