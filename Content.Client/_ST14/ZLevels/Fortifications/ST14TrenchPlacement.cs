/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using System.Linq;
using System.Numerics;
using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._ST14.ZLevels.Fortifications;

/// <summary>
/// The entrenching tool ghost from the build window: it only draws. The click is an ordinary use of the tool and the
/// server lays out the segment the way the tool is set (ST14TrenchSystem.UseAt)
/// </summary>
public sealed partial class ST14TrenchPlacementHijack : PlacementHijack
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public readonly EntityUid Tool;
    public readonly ST14DigMode Mode;

    public override bool CanRotate => true;

    public ST14TrenchPlacementHijack(EntityUid tool, ST14DigMode mode)
    {
        IoCManager.InjectDependencies(this);
        Tool = tool;
        Mode = mode;
    }

    // the placement runs without the editor context: a click is an ordinary use of the tool, see ST14TrenchMenuSystem
    public override bool HijackPlacementRequest(EntityCoordinates coordinates) => true;

    public override void StartHijack(PlacementManager manager)
    {
        base.StartHijack(manager);

        var icon = Mode == ST14DigMode.Steps ? "ST14TrenchSteps" : "ST14TrenchBlueprintMarker";
        if (_prototype.TryIndex(icon, out var proto))
            manager.CurrentTextures = _entMan.System<SpriteSystem>().GetPrototypeTextures(proto).ToList();
    }
}

/// <summary>
/// The ghost of a trench segment, a dug-out or steps on the cursor: every tile of the segment, turned with R. Steps
/// are drawn the way they climb out
/// </summary>
public sealed partial class ST14TrenchPlacementMode : SnapgridCenter
{
    [Dependency] private Robust.Client.Input.IInputManager _input = default!;

    public ST14TrenchPlacementMode(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);
        ValidPlaceColor = Color.FromHex("#e8dcae");
        InvalidPlaceColor = Color.FromHex("#ff3b3b");
    }

    public override bool HasLineMode => false;
    public override bool HasGridMode => false;

    // the server says what is wrong with a spot
    public override bool IsValidPosition(EntityCoordinates position) => true;

    public override void Render(in OverlayDrawArgs args)
    {
        var uid = pManager.CurrentPlacementOverlayEntity;
        if (pManager.Hijack is not ST14TrenchPlacementHijack hijack ||
            !pManager.EntityManager.TryGetComponent(uid, out SpriteComponent? sprite) ||
            !MouseCoords.IsValid(pManager.EntityManager))
        {
            return;
        }

        var sprites = pManager.EntityManager.System<SpriteSystem>();
        var direction = pManager.Direction;

        // the cursor itself, not the tile it snaps to: a dug-out is centred on it, as the server lays it out
        var world = pManager.EyeManager.PixelToMap(_input.MouseScreenPosition).Position;
        // a trench is as long and wide as the tool is set to in the build window
        var digger = pManager.EntityManager.GetComponentOrNull<ST14TrenchDiggerComponent>(hijack.Tool);
        var shape = ST14TrenchShapes.Shape(hijack.Mode, world, direction, digger?.TrenchLength ?? 3,
            digger?.DugoutSize ?? 3, digger?.TrenchWidth ?? 1);

        // a bunker snaps flush against the bunkers next to it, as the server lays it out
        if (hijack.Mode is ST14DigMode.Dugout or ST14DigMode.Corner && TryGetSurface(out var ground))
        {
            var size = digger?.DugoutSize ?? 3;
            shape = ST14TrenchShapes.BunkerShape(hijack.Mode,
                ST14TrenchShapes.SnappedBunkerStart(world, size, t => BunkerAt(ground, t)), direction, size);
        }

        if (hijack.Mode == ST14DigMode.Door)
        {
            DrawDoors(args.WorldHandle, world);
            return;
        }

        if (hijack.Mode == ST14DigMode.Steps)
        {
            // a ladder faces away from where it leads, as ST14TrenchSystem.PlaceSteps turns it
            var eye = args.Viewport.Eye?.Rotation ?? default;
            sprite.Color = ValidPlaceColor;
            foreach (var part in shape)
            {
                sprites.RenderSprite((uid.Value, sprite), args.WorldHandle, eye, direction.GetOpposite().ToAngle(), part + new Vector2(0.5f));
            }

            return;
        }

        DrawOutline(args.WorldHandle, shape, joins: DugJoin());
    }

    /// <summary>
    /// The doors a dug-out near the cursor can have, the middle of each side: the one nearest the cursor - the one a
    /// click cuts - is strung bright, the others faint. Nothing near a dug-out, nothing is drawn.
    /// </summary>
    private void DrawDoors(DrawingHandleWorld handle, Vector2 cursor)
    {
        var entMan = pManager.EntityManager;
        if (!TryGetSurface(out var surface))
            return;

        var roofs = entMan.System<Content.Client._ST14.ZLevels.Roof.ST14ClientZLevelsRoofSystem>();
        (Vector2i Center, int Size)? DugoutAt(Vector2i tile)
        {
            return roofs.TryGetRoofEntity(surface, tile, out var roof) &&
                   entMan.TryGetComponent<ST14DugoutPartComponent>(roof, out var part)
                ? (part.Center, part.Size)
                : null;
        }

        // as ST14TrenchSystem.CanCutDoor: untouched ground, or a trench closed off by a partition; never a dug-out built
        // on to this one, nor a door that is open already
        var dug = DugJoin();
        var zLevels = entMan.System<Content.Client._ST14.ZLevels.Core.ST14ClientZLevelsSystem>();
        var maps = entMan.System<SharedMapSystem>();
        Entity<Robust.Shared.Map.Components.MapGridComponent>? floor = zLevels.TryMapDown(surface, out var below) &&
                                          zLevels.TryGetLayerGrid(below.Owner, out var grid) ? grid : null;

        bool SeamAt(Vector2i tile, Vector2i side)
        {
            if (floor is not { } f)
                return false;

            var anchored = maps.GetAnchoredEntitiesEnumerator(f, f.Comp, maps.WorldToTile(f, f.Comp, tile + new Vector2(0.5f)));
            while (anchored.MoveNext(out var uid))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid.Value).EntityPrototype?.ID == "ST14DugoutSeam" &&
                    entMan.GetComponent<TransformComponent>(uid.Value).LocalRotation.GetCardinalDir().ToIntVec() == side)
                {
                    return true;
                }
            }

            return false;
        }

        bool Possible(Vector2i spot, Vector2i middle)
        {
            if (DugoutAt(spot) != null || DugoutAt(middle) == null)
                return false;

            if (dug?.Invoke(spot) != true)
                return true;

            return SeamAt(middle, spot - middle) || SeamAt(spot, middle - spot);
        }

        if (!ST14DugoutDoors.TryFind(cursor, DugoutAt, Possible, out var spots, out var door))
            return;

        foreach (var spot in spots)
        {
            if (spot != door)
                DrawOutline(handle, new[] { spot }, ValidPlaceColor.WithAlpha(0.35f));
        }

        DrawOutline(handle, new[] { door }, ValidPlaceColor);
    }

    /// <summary>
    /// Whether a world tile of the surface is dug out (a trench, a dug-out): a layout joins it with no string between
    /// </summary>
    private Func<Vector2i, bool>? DugJoin()
    {
        var entMan = pManager.EntityManager;
        if (!TryGetSurface(out var surface) ||
            !entMan.System<Content.Client._ST14.ZLevels.Core.ST14ClientZLevelsSystem>().TryGetLayerGrid(surface, out var grid))
        {
            return null;
        }

        var maps = entMan.System<SharedMapSystem>();
        var ground = grid.Value;
        return t => maps.TryGetTileRef(ground, ground.Comp, maps.WorldToTile(ground, ground.Comp, t + new Vector2(0.5f)), out var tileRef) &&
                    tileRef.Tile.IsEmpty;
    }

    /// <summary>
    /// The surface of the sector the player is on: their own level, or the one over the trench they stand in
    /// </summary>
    /// <summary>
    /// The bunker a tile of the surface belongs to, by the roof over it
    /// </summary>
    private (Vector2i Center, int Size)? BunkerAt(EntityUid surface, Vector2i tile)
    {
        var entMan = pManager.EntityManager;
        var roofs = entMan.System<Content.Client._ST14.ZLevels.Roof.ST14ClientZLevelsRoofSystem>();
        return roofs.TryGetRoofEntity(surface, tile, out var roof) &&
               entMan.TryGetComponent<ST14DugoutPartComponent>(roof, out var part)
            ? (part.Center, part.Size)
            : null;
    }

    private bool TryGetSurface(out EntityUid surface)
    {
        surface = default;
        var entMan = pManager.EntityManager;
        var zLevels = entMan.System<Content.Client._ST14.ZLevels.Core.ST14ClientZLevelsSystem>();
        if (IoCManager.Resolve<Robust.Client.Player.IPlayerManager>().LocalEntity is not { } player ||
            entMan.GetComponent<TransformComponent>(player).MapUid is not { } map ||
            !entMan.TryGetComponent<Content.Shared._ST14.ZLevels.Core.Components.ST14ZMapComponent>(map, out var zMap))
        {
            return false;
        }

        if (zMap.Depth == 0)
        {
            surface = map;
            return true;
        }

        if (zMap.Depth == -1 && zLevels.TryMapUp(map, out var above))
        {
            surface = above.Owner;
            return true;
        }

        return false;
    }

    /// <summary>
    /// String round the whole segment and a stake at each outer corner, like the layout it becomes
    /// </summary>
    private void DrawOutline(DrawingHandleWorld handle, IReadOnlyCollection<Vector2i> shape, Color? color = null, Func<Vector2i, bool>? joins = null)
    {
        const float inset = 3f / 32f;
        const float stake = 2.5f / 32f;
        var rope = color ?? ValidPlaceColor;
        var post = Color.FromHex("#6b4a2b").WithAlpha(rope.A);
        var set = new HashSet<Vector2i>(shape);
        // a tile of the shape, or something it joins (dug ground): no string between the two
        bool Inside(Vector2i t) => set.Contains(t) || joins?.Invoke(t) == true;

        foreach (var tile in shape)
        {
            var min = new Vector2(tile.X + inset, tile.Y + inset);
            var max = new Vector2(tile.X + 1 - inset, tile.Y + 1 - inset);
            var north = !Inside(tile + new Vector2i(0, 1));
            var south = !Inside(tile + new Vector2i(0, -1));
            var east = !Inside(tile + new Vector2i(1, 0));
            var west = !Inside(tile + new Vector2i(-1, 0));

            // a side on the edge runs to the tile border where the shape goes on, so tiles join up
            var left = west ? min.X : tile.X;
            var right = east ? max.X : tile.X + 1;
            var bottom = south ? min.Y : tile.Y;
            var top = north ? max.Y : tile.Y + 1;

            if (north)
                handle.DrawLine(new Vector2(left, max.Y), new Vector2(right, max.Y), rope);
            if (south)
                handle.DrawLine(new Vector2(left, min.Y), new Vector2(right, min.Y), rope);
            if (east)
                handle.DrawLine(new Vector2(max.X, bottom), new Vector2(max.X, top), rope);
            if (west)
                handle.DrawLine(new Vector2(min.X, bottom), new Vector2(min.X, top), rope);

            foreach (var (corner, a, b) in new[]
                     {
                         (new Vector2(max.X, max.Y), north, east),
                         (new Vector2(min.X, max.Y), north, west),
                         (new Vector2(max.X, min.Y), south, east),
                         (new Vector2(min.X, min.Y), south, west),
                     })
            {
                if (a && b)
                    handle.DrawRect(new Box2(corner - new Vector2(stake), corner + new Vector2(stake)), post);
            }

            // where the shape turns inward the string of the two neighbours meets in this tile: a short bend and a stake
            foreach (var (dx, dy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            {
                if (!Inside(tile + new Vector2i(dx, 0)) || !Inside(tile + new Vector2i(0, dy)) ||
                    Inside(tile + new Vector2i(dx, dy)))
                {
                    continue;
                }

                var x = dx > 0 ? max.X : min.X;
                var y = dy > 0 ? max.Y : min.Y;
                var corner = new Vector2(x, y);
                handle.DrawLine(corner, new Vector2(x, dy > 0 ? tile.Y + 1 : tile.Y), rope);
                handle.DrawLine(corner, new Vector2(dx > 0 ? tile.X + 1 : tile.X, y), rope);
                handle.DrawRect(new Box2(corner - new Vector2(stake), corner + new Vector2(stake)), post);
            }
        }
    }

    public void Rotate(bool clockwise)
    {
        var turns = clockwise ? 1 : 3;
        for (var i = 0; i < turns; i++)
        {
            pManager.Rotate();
        }
    }
}
