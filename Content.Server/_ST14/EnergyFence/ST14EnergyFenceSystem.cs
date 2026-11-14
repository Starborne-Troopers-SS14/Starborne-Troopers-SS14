using System.Numerics;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Audio;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Physics;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// Энергостены: пилоны берут питание от энергощитка с энергоблоком и тянут лучи к соседям на одной прямой.
/// Связи пересчитываются при установке/снятии пилона или щитка, при смене энергоблока и раз в секунду
/// (чтобы стена, выросшая между пилонами, рвала луч)
/// </summary>
public sealed partial class ST14EnergyFenceSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedBatterySystem _battery = default!;

    private static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Связь (западный/южный пилон, восточный/северный пилон) → сегменты луча между ними
    /// </summary>
    private readonly Dictionary<(EntityUid A, EntityUid B), List<EntityUid>> _links = new();

    private readonly HashSet<(EntityUid A, EntityUid B)> _wanted = new();
    private readonly List<(EntityUid A, EntityUid B, int Length, bool Vertical)> _newLinks = new();
    private readonly List<(EntityUid Box, MapId Map, Vector2 Position)> _activeBoxes = new();
    private readonly List<(EntityUid A, EntityUid B)> _toRemove = new();

    private bool _dirty;
    private TimeSpan _nextRecheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14EnergyPylonComponent, ComponentStartup>(OnChanged);
        SubscribeLocalEvent<ST14EnergyPylonComponent, ComponentShutdown>(OnChanged);
        SubscribeLocalEvent<ST14EnergyPylonComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14EnergyPylonComponent, ExaminedEvent>(OnPylonExamined);

        SubscribeLocalEvent<ST14EnergyBoxComponent, ComponentStartup>(OnChanged);
        SubscribeLocalEvent<ST14EnergyBoxComponent, ComponentShutdown>(OnChanged);
        SubscribeLocalEvent<ST14EnergyBoxComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ST14EnergyBoxComponent, EntInsertedIntoContainerMessage>(OnBoxContainerChanged);
        SubscribeLocalEvent<ST14EnergyBoxComponent, EntRemovedFromContainerMessage>(OnBoxContainerChanged);
        SubscribeLocalEvent<ST14EnergyBoxComponent, ExaminedEvent>(OnBoxExamined);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        InitializeBeam();
    }

    private void OnChanged<T>(EntityUid uid, T comp, EntityEventArgs args) where T : IComponent
    {
        _dirty = true;
    }

    private void OnAnchorChanged<T>(EntityUid uid, T comp, ref AnchorStateChangedEvent args) where T : IComponent
    {
        _dirty = true;
    }

    private void OnBoxContainerChanged(EntityUid uid, ST14EnergyBoxComponent comp, ContainerModifiedMessage args)
    {
        if (args.Container.ID != comp.BlockSlot)
            return;

        _dirty = true;
        UpdateBoxVisuals((uid, comp));
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _links.Clear();
        _nextHit.Clear();
    }

    private void OnPylonExamined(Entity<ST14EnergyPylonComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Box == null)
            args.PushMarkup(Loc.GetString("st14-energy-pylon-examine-unpowered"));
        else
            args.PushMarkup(Loc.GetString("st14-energy-pylon-examine-powered", ("links", ent.Comp.Links)));
    }

    private void OnBoxExamined(Entity<ST14EnergyBoxComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (GetBlock(ent) is not { } block)
        {
            args.PushMarkup(Loc.GetString("st14-energy-box-examine-empty"));
            return;
        }

        var percent = (int) MathF.Round(_battery.GetChargeLevel(block) * 100f);
        args.PushMarkup(Loc.GetString("st14-energy-box-examine-charge", ("percent", percent)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (_dirty || now >= _nextRecheck)
        {
            _dirty = false;
            _nextRecheck = now + RecheckDelay;
            Recompute();
        }

        UpdateBeams(now);
    }

    /// <summary>
    /// Энергоблок в щитке, если он есть
    /// </summary>
    public EntityUid? GetBlock(Entity<ST14EnergyBoxComponent> box)
    {
        return _slots.GetItemOrNull(box.Owner, box.Comp.BlockSlot);
    }

    private bool IsBoxActive(Entity<ST14EnergyBoxComponent> box)
    {
        return Transform(box).Anchored && GetBlock(box) is { } block && _battery.GetCharge(block).Charge > 0f;
    }

    private void UpdateBoxVisuals(Entity<ST14EnergyBoxComponent> box)
    {
        _appearance.SetData(box, ST14EnergyBoxVisuals.HasBlock, GetBlock(box) != null);
        _appearance.SetData(box, ST14EnergyBoxVisuals.Active, IsBoxActive(box));
    }

    /// <summary>
    /// Заново раздаёт питание пилонам и строит/рвёт лучи
    /// </summary>
    private void Recompute()
    {
        // Работающие щитки
        _activeBoxes.Clear();
        var boxes = EntityQueryEnumerator<ST14EnergyBoxComponent, TransformComponent>();
        while (boxes.MoveNext(out var uid, out var box, out var xform))
        {
            var active = IsBoxActive((uid, box));
            _appearance.SetData(uid, ST14EnergyBoxVisuals.Active, active);
            if (active)
                _activeBoxes.Add((uid, xform.MapID, _transform.GetWorldPosition(xform)));
        }

        // Питание пилонов: ближайший работающий щиток в радиусе
        var pylons = EntityQueryEnumerator<ST14EnergyPylonComponent, TransformComponent>();
        while (pylons.MoveNext(out var uid, out var pylon, out var xform))
        {
            pylon.Box = null;
            if (!xform.Anchored || xform.GridUid == null || TerminatingOrDeleted(uid))
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var best = pylon.PowerRange * pylon.PowerRange;
            foreach (var (box, map, boxPos) in _activeBoxes)
            {
                if (map != xform.MapID)
                    continue;

                var dist = (boxPos - pos).LengthSquared();
                if (dist > best)
                    continue;

                best = dist;
                pylon.Box = box;
            }
        }

        // Какие лучи должны стоять
        _wanted.Clear();
        _newLinks.Clear();
        pylons = EntityQueryEnumerator<ST14EnergyPylonComponent, TransformComponent>();
        while (pylons.MoveNext(out var uid, out var pylon, out var xform))
        {
            if (pylon.Box == null || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;

            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            TryLink(uid, pylon, (gridUid, grid), tile, new Vector2i(1, 0), false);
            TryLink(uid, pylon, (gridUid, grid), tile, new Vector2i(0, 1), true);
        }

        // Рвём лишние
        _toRemove.Clear();
        foreach (var (key, segments) in _links)
        {
            if (_wanted.Contains(key))
                continue;

            foreach (var segment in segments)
                QueueDel(segment);

            _toRemove.Add(key);
        }

        foreach (var key in _toRemove)
            _links.Remove(key);

        // Строим новые
        foreach (var (a, b, length, vertical) in _newLinks)
        {
            if (_links.ContainsKey((a, b)))
                continue;

            _links[(a, b)] = SpawnBeam(a, b, length, vertical);
        }

        // Счётчики связей и визуал пилонов
        pylons = EntityQueryEnumerator<ST14EnergyPylonComponent, TransformComponent>();
        while (pylons.MoveNext(out var uid, out var pylon, out _))
        {
            pylon.Links = 0;
        }

        foreach (var (a, b) in _links.Keys)
        {
            if (TryComp<ST14EnergyPylonComponent>(a, out var pa))
                pa.Links++;
            if (TryComp<ST14EnergyPylonComponent>(b, out var pb))
                pb.Links++;
        }

        pylons = EntityQueryEnumerator<ST14EnergyPylonComponent, TransformComponent>();
        while (pylons.MoveNext(out var uid, out var pylon, out _))
        {
            var linked = pylon.Links > 0;
            _appearance.SetData(uid, ST14EnergyPylonVisuals.Powered, pylon.Box != null);
            _appearance.SetData(uid, ST14EnergyPylonVisuals.Linked, linked);
            _ambient.SetAmbience(uid, linked);
            _light.SetEnabled(uid, pylon.Box != null);
        }

        CleanupHits();
    }

    /// <summary>
    /// Ищет ближайший пилон в направлении <paramref name="step"/>. Твёрдая стена на пути или незапитанный пилон обрывают поиск
    /// </summary>
    private void TryLink(EntityUid uid, ST14EnergyPylonComponent pylon, Entity<MapGridComponent> grid, Vector2i origin, Vector2i step, bool vertical)
    {
        for (var i = 1; i <= pylon.MaxLength; i++)
        {
            var tile = origin + step * i;
            var anchored = _map.GetAnchoredEntities(grid, tile);
            var blocked = false;

            while (anchored.MoveNext(out var other))
            {
                if (TryComp<ST14EnergyPylonComponent>(other, out var otherPylon))
                {
                    if (otherPylon.Box == null || TerminatingOrDeleted(other.Value))
                        return;

                    _wanted.Add((uid, other.Value));
                    _newLinks.Add((uid, other.Value, i, vertical));
                    return;
                }

                if (IsWall(other.Value))
                    blocked = true;
            }

            if (blocked)
                return;
        }
    }

    private bool IsWall(EntityUid uid)
    {
        return TryComp<PhysicsComponent>(uid, out var physics)
               && physics.CanCollide
               && physics.Hard
               && (physics.CollisionLayer & (int) CollisionGroup.Impassable) != 0;
    }

    private List<EntityUid> SpawnBeam(EntityUid a, EntityUid b, int length, bool vertical)
    {
        var segments = new List<EntityUid>();
        var pylon = Comp<ST14EnergyPylonComponent>(a);
        var xform = Transform(a);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return segments;

        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var step = vertical ? new Vector2i(0, 1) : new Vector2i(1, 0);
        var proto = vertical ? pylon.BeamVertical : pylon.BeamHorizontal;

        for (var i = 1; i < length; i++)
        {
            var segment = Spawn(proto, _map.GridTileToLocal(gridUid, grid, origin + step * i));
            if (TryComp<ST14EnergyBeamComponent>(segment, out var beam))
            {
                beam.Source = a;
                beam.Vertical = vertical;
            }

            segments.Add(segment);
        }

        return segments;
    }
}
