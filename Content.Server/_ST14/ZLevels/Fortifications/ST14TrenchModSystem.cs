/*
 * Starborne Troopers trench modifications on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Shared.Map;

namespace Content.Server._ST14.ZLevels.Fortifications;

/// <summary>
/// Trench modifications (barbed wire, a barricade; ST14TrenchModComponent): built from the surface over a trench they
/// go down onto its floor, on the same tile, and are fixed there. Filling the trench in takes them away
/// (ST14TrenchSystem.Fill.cs).
/// </summary>
public sealed partial class ST14TrenchModSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14TrenchModComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ST14TrenchModComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent);
        if (!ST14Dugouts.TryGetTrench(EntityManager, xform.Coordinates, out _, out var floor, out var worldTile) ||
            xform.MapUid == floor && xform.Anchored)
        {
            return;
        }

        var rotation = xform.LocalRotation;
        _transform.SetCoordinates(ent, xform, new EntityCoordinates(floor, worldTile + new System.Numerics.Vector2(0.5f)), rotation);
        _transform.AnchorEntity((ent, xform));
    }
}
