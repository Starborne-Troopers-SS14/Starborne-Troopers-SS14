/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Client._ST14.ZLevels.Core;
using Content.Client._ST14.ZLevels.Roof;
using Content.Shared._ST14.ZLevels.Core.Components;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._ST14.ZLevels.Fortifications;

/// <summary>
/// From inside a dug-out, under its roof, the soldier sees nobody and nothing on the surface: people and things up
/// there are not drawn for them (the ground itself is darkened by <see cref="ST14TrenchSightOverlay"/>). From an open
/// trench, or out of the dug-out, everything is drawn as ever.
/// </summary>
public sealed partial class ST14TrenchSurfaceHidingSystem : EntitySystem
{
    /// <summary>
    /// How far round the player the surface is looked through, wider than any screen
    /// </summary>
    private const float Range = 30f;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ST14ClientZLevelsSystem _zLevels = default!;
    [Dependency] private ST14ClientZLevelsRoofSystem _roofs = default!;

    private readonly HashSet<EntityUid> _hidden = new();
    private readonly HashSet<EntityUid> _wanted = new();
    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private readonly HashSet<Entity<ItemComponent>> _items = new();
    private TimeSpan _next;

    public override void Shutdown()
    {
        base.Shutdown();
        ShowAll();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_timing.RealTime < _next)
            return;

        _next = _timing.RealTime + Interval;

        if (_player.LocalEntity is not { } player || Transform(player).MapUid is not { } map ||
            !TryComp<ST14ZMapComponent>(map, out var zMap) || zMap.Depth >= 0 ||
            !_zLevels.TryMapUp(map, out var above))
        {
            ShowAll();
            return;
        }

        // only under the roof of a dug-out
        var position = _transform.GetWorldPosition(player);
        var tile = new Vector2i((int) MathF.Floor(position.X), (int) MathF.Floor(position.Y));
        if (!_roofs.TryGetRoofEntity(above.Owner, tile, out _))
        {
            ShowAll();
            return;
        }

        var coords = new MapCoordinates(position, Transform(above).MapID);
        _wanted.Clear();
        _mobs.Clear();
        _items.Clear();
        _lookup.GetEntitiesInRange(coords, Range, _mobs, LookupFlags.Uncontained);
        _lookup.GetEntitiesInRange(coords, Range, _items, LookupFlags.Uncontained);

        foreach (var mob in _mobs)
        {
            _wanted.Add(mob.Owner);
        }

        foreach (var item in _items)
        {
            _wanted.Add(item.Owner);
        }

        foreach (var uid in _hidden)
        {
            if (!_wanted.Contains(uid))
                SetShown(uid, true);
        }

        _hidden.RemoveWhere(uid => !_wanted.Contains(uid));
        foreach (var uid in _wanted)
        {
            // only what this system hid is shown again: something hidden for another reason stays hidden
            if (_hidden.Contains(uid) || !TryComp<SpriteComponent>(uid, out var sprite) || !sprite.Visible)
                continue;

            SetShown(uid, false);
            _hidden.Add(uid);
        }
    }

    private void ShowAll()
    {
        foreach (var uid in _hidden)
        {
            SetShown(uid, true);
        }

        _hidden.Clear();
    }

    private void SetShown(EntityUid uid, bool shown)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetVisible((uid, sprite), shown);
    }
}
