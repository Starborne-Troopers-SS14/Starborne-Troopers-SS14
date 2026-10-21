using System.Numerics;
using Content.Shared._ST14.ST14Resources;
using Content.Shared._ST14.ST14Resources.Components;
using Content.Shared._ST14.ST14Resources.EntitySystems;
using Content.Shared.Audio;
using Content.Shared.Camera;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.ST14Resources;

public sealed partial class ST14ResourceExtractorSystem : SharedST14ResourceExtractorSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private ST14ResourceEconomySystem _economy = default!;

    private readonly HashSet<Entity<CameraRecoilComponent>> _shakeTargets = new();
    private readonly HashSet<Entity<ST14ResourceFieldComponent>> _fields = new();

    private static readonly ST14ResourceExtractorVisuals[] SlotVisuals =
    {
        ST14ResourceExtractorVisuals.Slot1Fill,
        ST14ResourceExtractorVisuals.Slot2Fill,
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14ResourceExtractorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, ST14ResourceExtractorToggleMessage>(OnToggleMessage);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, ST14ResourceExtractorSelectSlotMessage>(OnSelectSlotMessage);
        SubscribeLocalEvent<ST14ResourceExtractorComponent, ST14ResourceExtractorSetAutoSwitchMessage>(OnSetAutoSwitchMessage);
    }

    private void OnMapInit(Entity<ST14ResourceExtractorComponent> ent, ref MapInitEvent args)
    {
        _appearance.SetData(ent, ST14ResourceExtractorVisuals.Extracting, ent.Comp.Extracting);
        _ambient.SetAmbience(ent, ent.Comp.Extracting);
        SetLight(ent, ent.Comp.Extracting);
        UpdateSlotVisuals(ent);
    }

    private void OnContainerModified(EntityUid uid, ST14ResourceExtractorComponent comp, ContainerModifiedMessage args)
    {
        var index = GetSlotIndex(args.Container.ID);
        if (index < 0 || TerminatingOrDeleted(uid))
            return;

        var ent = new Entity<ST14ResourceExtractorComponent>(uid, comp);
        UpdateSlotVisuals(ent);

        if (args is EntRemovedFromContainerMessage && comp.Extracting && index == comp.ActiveSlot && !TryAutoSwitch(ent))
        {
            SetExtracting(ent, false);
            return;
        }

        UpdateUi(ent);
    }

    private void OnUiOpened(Entity<ST14ResourceExtractorComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent, force: true);
    }

    private void OnToggleMessage(Entity<ST14ResourceExtractorComponent> ent, ref ST14ResourceExtractorToggleMessage args)
    {
        if (ent.Comp.Extracting)
        {
            SetExtracting(ent, false);
            return;
        }

        if (!CanFill(ent, ent.Comp.ActiveSlot) && !TryAutoSwitch(ent))
        {
            var reason = TryGetCapsule(ent, ent.Comp.ActiveSlot, out _)
                ? "st14-extractor-capsule-full"
                : "st14-extractor-no-capsule";
            _popup.PopupEntity(Loc.GetString(reason), ent, args.Actor);
            return;
        }

        if (ent.Comp.RequireField && !IsOnField(ent))
        {
            _popup.PopupEntity(Loc.GetString("st14-extractor-no-field"), ent, args.Actor);
            return;
        }

        SetExtracting(ent, true);
    }

    private void OnSelectSlotMessage(Entity<ST14ResourceExtractorComponent> ent, ref ST14ResourceExtractorSelectSlotMessage args)
    {
        if (args.Slot < 0 || args.Slot >= ST14ResourceExtractorComponent.SlotCount || args.Slot == ent.Comp.ActiveSlot)
            return;

        ent.Comp.ActiveSlot = args.Slot;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnSetAutoSwitchMessage(Entity<ST14ResourceExtractorComponent> ent, ref ST14ResourceExtractorSetAutoSwitchMessage args)
    {
        ent.Comp.AutoSwitch = args.Enabled;
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnExamined(Entity<ST14ResourceExtractorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.Extracting
            ? "st14-extractor-examine-extracting"
            : "st14-extractor-examine-idle"));

        var percent = (int) MathF.Round(_economy.GetExtractionModifier(ent) * 100);
        args.PushMarkup(Loc.GetString("st14-extractor-examine-modifier",
            ("modifier", percent > 0 ? $"+{percent}" : percent.ToString())));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ST14ResourceExtractorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Extracting || now < comp.NextHitTime)
                continue;

            comp.NextHitTime += comp.HitInterval;
            DoHit((uid, comp));
        }
    }

    private void DoHit(Entity<ST14ResourceExtractorComponent> ent)
    {
        if (!CanFill(ent, ent.Comp.ActiveSlot) && !TryAutoSwitch(ent))
        {
            SetExtracting(ent, false);
            return;
        }

        TryGetCapsule(ent, ent.Comp.ActiveSlot, out var capsule);
        var amount = GetRate(ent) * ent.Comp.HitInterval.TotalSeconds;
        var added = Capsules.TryAdd(capsule, ent.Comp.ResourceType, amount);

        var leftover = amount - added;
        if (Capsules.IsFull(capsule) && TryAutoSwitch(ent) && leftover > FixedPoint2.Zero &&
            TryGetCapsule(ent, ent.Comp.ActiveSlot, out var next))
        {
            Capsules.TryAdd(next, ent.Comp.ResourceType, leftover);
        }

        UpdateSlotVisuals(ent);
        PlayHitEffects(ent);

        if (!CanFill(ent, ent.Comp.ActiveSlot))
            SetExtracting(ent, false);
        else
            UpdateUi(ent);
    }

    private bool TryAutoSwitch(Entity<ST14ResourceExtractorComponent> ent)
    {
        var other = OtherSlot(ent.Comp.ActiveSlot);
        if (!ent.Comp.AutoSwitch || !CanFill(ent, other))
            return false;

        ent.Comp.ActiveSlot = other;
        Dirty(ent);
        return true;
    }

    private FixedPoint2 GetRate(Entity<ST14ResourceExtractorComponent> ent)
    {
        return ent.Comp.ExtractionRate * _economy.GetExtractionMultiplier(ent);
    }

    private void SetExtracting(Entity<ST14ResourceExtractorComponent> ent, bool extracting)
    {
        ent.Comp.Extracting = extracting;
        if (extracting)
            ent.Comp.NextHitTime = _timing.CurTime + ent.Comp.HitInterval;

        Dirty(ent);
        _appearance.SetData(ent, ST14ResourceExtractorVisuals.Extracting, extracting);
        _ambient.SetAmbience(ent, extracting);
        SetLight(ent, extracting);
        UpdateUi(ent);
    }

    /// <summary>
    /// Включает/выключает свет ламп, если у бура есть PointLight.
    /// </summary>
    private void SetLight(EntityUid uid, bool enabled)
    {
        if (_light.TryGetLight(uid, out var light))
            _light.SetEnabled(uid, enabled, light);
    }

    /// <summary>
    /// Передаёт на клиент заполнение капсулы в каждом слоте: 0..1, или -1 если слот пуст.
    /// </summary>
    private void UpdateSlotVisuals(Entity<ST14ResourceExtractorComponent> ent)
    {
        for (var i = 0; i < SlotVisuals.Length; i++)
        {
            var fraction = -1f;
            if (TryGetCapsule(ent, i, out var capsule))
            {
                fraction = capsule.Comp.Capacity > FixedPoint2.Zero
                    ? (capsule.Comp.Amount / capsule.Comp.Capacity).Float()
                    : 0f;
            }

            _appearance.SetData(ent, SlotVisuals[i], fraction);
        }
    }

    private ST14ResourceExtractorStatus GetStatus(Entity<ST14ResourceExtractorComponent> ent)
    {
        if (ent.Comp.Extracting)
            return ST14ResourceExtractorStatus.Working;

        var active = ent.Comp.ActiveSlot;
        var activeFull = TryGetCapsule(ent, active, out var capsule) && Capsules.IsFull(capsule);
        var canSwitch = ent.Comp.AutoSwitch && CanFill(ent, OtherSlot(active));

        return activeFull && !canSwitch
            ? ST14ResourceExtractorStatus.NoSpace
            : ST14ResourceExtractorStatus.Off;
    }

    private void UpdateUi(Entity<ST14ResourceExtractorComponent> ent, bool force = false)
    {
        if (!force && !_ui.IsUiOpen(ent.Owner, ST14ResourceExtractorUIKey.Key))
            return;

        var slots = new ST14ResourceExtractorSlotState[ST14ResourceExtractorComponent.SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            slots[i] = TryGetCapsule(ent, i, out var capsule)
                ? new ST14ResourceExtractorSlotState(true, capsule.Comp.Amount, capsule.Comp.Capacity)
                : new ST14ResourceExtractorSlotState(false, FixedPoint2.Zero, FixedPoint2.Zero);
        }

        var state = new ST14ResourceExtractorBoundUserInterfaceState(
            GetStatus(ent),
            ent.Comp.Extracting ? GetRate(ent).Float() : 0f,
            ent.Comp.ActiveSlot,
            ent.Comp.AutoSwitch,
            slots,
            ent.Comp.ResourceType);

        _ui.SetUiState(ent.Owner, ST14ResourceExtractorUIKey.Key, state);
    }

    private void PlayHitEffects(Entity<ST14ResourceExtractorComponent> ent)
    {
        if (ent.Comp.HitSound != null)
            _audio.PlayPvs(ent.Comp.HitSound, ent, ent.Comp.HitSound.Params.WithMaxDistance(ent.Comp.SoundRadius));

        if (ent.Comp.ShakeStrength <= 0f)
            return;

        _shakeTargets.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.ShakeRadius, _shakeTargets);
        foreach (var target in _shakeTargets)
        {
            var kick = _random.NextAngle().RotateVec(new Vector2(ent.Comp.ShakeStrength, 0f));
            _recoil.KickCamera(target, kick, target.Comp);
        }
    }

    private bool IsOnField(Entity<ST14ResourceExtractorComponent> ent)
    {
        _fields.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.FieldSearchRange, _fields);
        foreach (var field in _fields)
        {
            if (field.Comp.ResourceType == ent.Comp.ResourceType)
                return true;
        }

        return false;
    }
}
