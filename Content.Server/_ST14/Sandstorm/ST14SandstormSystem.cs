using Content.Shared._ST14.Sandstorm;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._ST14.Sandstorm;

/// <summary>
/// Works out the depth of every sandstorm tile: tiles next to open ground are the fringe, which troopers can wade into
/// and bullets fly through, the rest is a solid wall. Re-run around a tile whenever one is placed or removed
/// </summary>
public sealed partial class ST14SandstormSystem : EntitySystem
{
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private OccluderSystem _occluder = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private const string FixtureId = "fix1";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14SandstormWallComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14SandstormWallComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14SandstormWallComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<ST14SandstormWallComponent> ent, ref MapInitEvent args)
    {
        RefreshAround(ent, true);
    }

    private void OnAnchorChanged(Entity<ST14SandstormWallComponent> ent, ref AnchorStateChangedEvent args)
    {
        RefreshAround(ent, args.Anchored);
    }

    private void OnShutdown(Entity<ST14SandstormWallComponent> ent, ref ComponentShutdown args)
    {
        RefreshAround(ent, false);
    }

    /// <summary>
    /// Recomputes this tile and every storm tile close enough for its depth to change with it
    /// </summary>
    private void RefreshAround(Entity<ST14SandstormWallComponent> ent, bool includeSelf)
    {
        // a map still being loaded or edited uninitialised keeps the prototype data; MapInit works the depth out later
        if (LifeStage(ent) < EntityLifeStage.MapInitialized)
            return;

        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var depth = ent.Comp.FringeDepth;

        for (var dx = -depth; dx <= depth; dx++)
        {
            for (var dy = -depth; dy <= depth; dy++)
            {
                var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, center + new Vector2i(dx, dy));
                while (anchored.MoveNext(out var other))
                {
                    if (!TryComp<ST14SandstormWallComponent>(other, out var wall) ||
                        !includeSelf && other.Value == ent.Owner ||
                        TerminatingOrDeleted(other.Value))
                        continue;

                    Refresh((other.Value, wall), gridUid, grid, includeSelf ? null : ent.Owner);
                }
            }
        }
    }

    private void Refresh(Entity<ST14SandstormWallComponent> ent, EntityUid gridUid, MapGridComponent grid, EntityUid? leaving)
    {
        var tile = _map.TileIndicesFor(gridUid, grid, Transform(ent).Coordinates);
        var depth = ent.Comp.FringeDepth;

        var fringe = false;
        for (var dx = -depth; dx <= depth && !fringe; dx++)
        {
            for (var dy = -depth; dy <= depth && !fringe; dy++)
            {
                if (!HasStorm(gridUid, grid, tile + new Vector2i(dx, dy), leaving))
                    fringe = true;
            }
        }

        if (ent.Comp.Fringe == fringe && ent.Comp.Computed)
            return;

        ent.Comp.Computed = true;
        ent.Comp.Fringe = fringe;
        Dirty(ent);

        // the fringe stops being solid: people wade in, bullets fly on, sight goes through
        if (_fixtures.GetFixtureOrNull(ent, FixtureId) is { } fixture)
            _physics.SetHard(ent, fixture, !fringe);

        _occluder.SetEnabled(ent, !fringe);
    }

    private bool HasStorm(EntityUid gridUid, MapGridComponent grid, Vector2i tile, EntityUid? leaving)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (uid.Value != leaving && HasComp<ST14SandstormWallComponent>(uid.Value) && !TerminatingOrDeleted(uid.Value))
                return true;
        }

        return false;
    }
}
