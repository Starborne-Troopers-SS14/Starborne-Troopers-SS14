/*
 * Starborne Troopers bunker roofs on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Client._ST14.ZLevels.Core;
using Content.Client._ST14.ZLevels.Roof;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared._ST14.ZLevels.Fortifications;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._ST14.ZLevels.Fortifications;

/// <summary>
/// Whoever is inside a line of trenches sees through the roofs of it: the roof of every dug-out the player's trench
/// leads to (straight or through other trenches) turns faint, the roofs of other lines stay. Under a roof - in a dug-out
/// or a bunker - that roof and every roof joined to it are gone and the inside is seen. Out on the surface nothing
/// changes.
/// </summary>
public sealed partial class ST14BunkerRoofVisualsSystem : EntitySystem
{
    /// <summary>
    /// How see-through a roof over the player's own line is
    /// </summary>
    private const float SeeThroughAlpha = 0.2f;

    /// <summary>
    /// A bigger line than this is cut off: the fill stays cheap
    /// </summary>
    private const int MaxTiles = 600;

    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private Robust.Client.Graphics.IOverlayManager _overlays = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14ClientZLevelsSystem _zLevels = default!;
    [Dependency] private ST14ClientZLevelsRoofSystem _roofs = default!;

    private readonly HashSet<EntityUid> _changed = new();
    private readonly Dictionary<EntityUid, float> _wanted = new();
    private readonly Queue<Vector2i> _queue = new();
    private readonly HashSet<Vector2i> _seen = new();

    private (EntityUid Map, Vector2i Tile, int Roofs) _last;

    private static readonly Vector2i[] Sides = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];

    public override void Initialize()
    {
        base.Initialize();

        // the view along a trench, round its corners
        _overlays.AddOverlay(new ST14TrenchSightOverlay(EntityManager, _player));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<ST14TrenchSightOverlay>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } player || Transform(player).MapUid is not { } map ||
            !_zLevels.TryMapUp(map, out var above) || _roofs.RoofEntitiesOn(above.Owner) is not { } roofs)
        {
            ShowAll();
            _last = default;
            UpdateTops(null, null);
            return;
        }

        var world = _transform.GetWorldPosition(player);
        var tile = new Vector2i((int) MathF.Floor(world.X), (int) MathF.Floor(world.Y));
        var key = (above.Owner, tile, roofs.Count);
        if (key == _last)
        {
            UpdateTops(above.Owner, roofs);
            return;
        }

        _last = key;
        _wanted.Clear();

        // in a trench: every roof of the line it belongs to (every tile dug out of the ground above, roofed or open)
        // is see-through
        if (TryComp<ST14ZMapComponent>(map, out var zMap) && zMap.Depth < 0 && _zLevels.TryGetLayerGrid(above.Owner, out var grid))
        {
            var ground = grid.Value;
            Fill(tile, t => IsDug(ground, t), roofs, SeeThroughAlpha);
        }

        // under a roof: that roof and every roof joined to it are gone, the inside is seen
        // every dug-out built on to this one is the same building: all their roofs go
        if (roofs.ContainsKey(tile))
            Fill(tile, roofs.ContainsKey, roofs, 0f);

        foreach (var uid in _changed)
        {
            if (!_wanted.ContainsKey(uid))
                SetAlpha(uid, 1f);
        }

        _changed.Clear();
        foreach (var (uid, alpha) in _wanted)
        {
            SetAlpha(uid, alpha);
            _changed.Add(uid);
        }

        UpdateTops(above.Owner, roofs);
    }

    /// <summary>
    /// What stands on a roof (the post of a garrison, the tower of the observation bunker) fades and goes with it
    /// </summary>
    private void UpdateTops(EntityUid? surface, IReadOnlyDictionary<Vector2i, EntityUid>? roofs)
    {
        var tops = EntityQueryEnumerator<ST14BunkerTopComponent, SpriteComponent, TransformComponent>();
        while (tops.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var alpha = 1f;
            if (surface != null && roofs != null && xform.MapUid == surface)
            {
                var pos = _transform.GetWorldPosition(xform);
                var tile = new Vector2i((int) MathF.Floor(pos.X), (int) MathF.Floor(pos.Y));
                if (roofs.TryGetValue(tile, out var roof) && _wanted.TryGetValue(roof, out var roofAlpha))
                    alpha = roofAlpha;
            }

            if (!MathHelper.CloseTo(sprite.Color.A, alpha))
                _sprite.SetColor((uid, sprite), sprite.Color.WithAlpha(alpha));
        }
    }

    /// <summary>
    /// Every roof on the tiles joined to <paramref name="start"/> through tiles that are <paramref name="inside"/>
    /// gets <paramref name="alpha"/>
    /// </summary>
    private void Fill(Vector2i start, Func<Vector2i, bool> inside, IReadOnlyDictionary<Vector2i, EntityUid> roofs, float alpha)
    {
        _seen.Clear();
        _queue.Clear();
        if (!inside(start))
            return;

        _queue.Enqueue(start);
        _seen.Add(start);
        while (_queue.TryDequeue(out var next) && _seen.Count < MaxTiles)
        {
            if (roofs.TryGetValue(next, out var roof))
                _wanted[roof] = alpha;

            foreach (var side in Sides)
            {
                var neighbour = next + side;
                if (!_seen.Contains(neighbour) && inside(neighbour))
                {
                    _seen.Add(neighbour);
                    _queue.Enqueue(neighbour);
                }
            }
        }
    }

    /// <summary>
    /// The ground of the surface is dug out there (a trench or a dug-out under its roof)
    /// </summary>
    private bool IsDug(Entity<MapGridComponent> ground, Vector2i worldTile)
    {
        var index = _map.WorldToTile(ground, ground.Comp, worldTile + new System.Numerics.Vector2(0.5f));
        // off the grid is not a trench: the line ends there
        return _map.TryGetTileRef(ground, ground.Comp, index, out var tileRef) && tileRef.Tile.IsEmpty;
    }

    private void ShowAll()
    {
        foreach (var uid in _changed)
        {
            SetAlpha(uid, 1f);
        }

        _changed.Clear();
    }

    private void SetAlpha(EntityUid uid, float alpha)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite) && HasComp<ST14BunkerRoofComponent>(uid))
            _sprite.SetColor((uid, sprite), sprite.Color.WithAlpha(alpha));
    }
}
