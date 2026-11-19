using Content.Shared._ST14.Hellwalls;
using Robust.Shared.Map.Components;

namespace Content.Server._ST14.Hellwalls;

public sealed partial class ST14HellGateSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14HellGateComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14HellGateComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<ST14HellGateComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14HellGateComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<ST14HellGateComponent> ent, ref MapInitEvent args)
    {
        Rebuild(ent);
    }

    private void OnMove(Entity<ST14HellGateComponent> ent, ref MoveEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || args.NewRotation == args.OldRotation)
            return;

        Rebuild(ent);
    }

    private void OnAnchorChanged(Entity<ST14HellGateComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || args.Detaching)
            return;

        Rebuild(ent);
    }

    private void OnShutdown(Entity<ST14HellGateComponent> ent, ref ComponentShutdown args)
    {
        ClearParts(ent);
    }

    private void Rebuild(Entity<ST14HellGateComponent> ent)
    {
        ClearParts(ent);

        var xform = Transform(ent);
        if (!xform.Anchored ||
            xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var facing = xform.LocalRotation.GetCardinalDir();
        var side = facing.GetClockwise90Degrees().ToIntVec();
        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);

        for (var i = 0; i < ent.Comp.Parts.Count; i++)
        {
            var coords = _map.GridTileToLocal(gridUid, grid, origin + side * i);
            ent.Comp.Spawned.Add(SpawnAttachedTo(ent.Comp.Parts[i], coords, rotation: facing.ToAngle()));
        }
    }

    private void ClearParts(Entity<ST14HellGateComponent> ent)
    {
        foreach (var part in ent.Comp.Spawned)
        {
            if (!TerminatingOrDeleted(part))
                QueueDel(part);
        }

        ent.Comp.Spawned.Clear();
    }
}
