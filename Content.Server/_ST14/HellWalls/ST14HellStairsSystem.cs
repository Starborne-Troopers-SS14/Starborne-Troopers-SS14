using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._ST14.Hellwalls;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.Hellwalls;

public sealed partial class ST14HellStairsSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14HellStairsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14HellStairsComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<ST14HellStairsComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14HellStairsComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<ST14HellStairsComponent> ent, ref MapInitEvent args)
    {
        Rebuild(ent);
    }

    private void OnMove(Entity<ST14HellStairsComponent> ent, ref MoveEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || args.NewRotation == args.OldRotation)
            return;

        Rebuild(ent);
    }

    private void OnAnchorChanged(Entity<ST14HellStairsComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || args.Detaching)
            return;

        Rebuild(ent);
    }

    private void OnShutdown(Entity<ST14HellStairsComponent> ent, ref ComponentShutdown args)
    {
        ClearSteps(ent);
    }

    private void Rebuild(Entity<ST14HellStairsComponent> ent)
    {
        ClearSteps(ent);

        var xform = Transform(ent);
        if (!xform.Anchored ||
            xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var dir = xform.LocalRotation.GetCardinalDir();
        var rotation = dir.ToAngle();
        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var count = ent.Comp.Steps;

        for (var i = 1; i <= count; i++)
        {
            var coords = _map.GridTileToLocal(gridUid, grid, origin + dir.ToIntVec() * i);
            var step = SpawnAttachedTo(i == 1 ? ent.Comp.Flight : ent.Comp.Step, coords, rotation: rotation);

            var ground = EnsureComp<CEZLevelHighGroundComponent>(step);
            ground.HeightCurve = new List<float>
            {
                ent.Comp.Height * (count - i + 1) / count,
                ent.Comp.Height * (count - i) / count,
            };
            ground.Stick = true;
            Dirty(step, ground);

            ent.Comp.Spawned.Add(step);
        }
    }

    private void ClearSteps(Entity<ST14HellStairsComponent> ent)
    {
        foreach (var step in ent.Comp.Spawned)
        {
            if (!TerminatingOrDeleted(step))
                QueueDel(step);
        }

        ent.Comp.Spawned.Clear();
    }
}
