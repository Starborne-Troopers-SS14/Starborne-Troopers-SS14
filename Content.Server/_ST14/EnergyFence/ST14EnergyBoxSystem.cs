using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._ST14.EnergyFence;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._ST14.EnergyFence;

/// <summary>
/// Power panel. In the power net it is an APC: <c>BatteryCharger</c> on MV takes power from field generators,
/// <c>Apc</c> + <c>PowerProvider</c> on LV feed the pylons. The panel's <c>PowerNetworkBattery</c> has no storage of its
/// own: before every power tick it takes the capacity and charge of the energy block in its slot, after the tick the
/// charge goes back. No block, no current at all; breaker off, nothing goes out.
/// The power solver passes the generators' power straight through, puts the surplus into the block and takes from the
/// block what the generators lack
/// </summary>
public sealed partial class ST14EnergyBoxSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedBatterySystem _battery = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    /// <summary>
    /// How often the block's own battery is told its charge while it sits in a panel
    /// </summary>
    private static readonly TimeSpan FlushDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan VisualsDelay = TimeSpan.FromSeconds(0.5);

    public const int ChargeLevels = 5;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NetworkBatteryPreSync>(OnPreSync);
        SubscribeLocalEvent<NetworkBatteryPostSync>(OnPostSync);

        SubscribeLocalEvent<ST14EnergyBoxComponent, MapInitEvent>(OnBoxMapInit);
        SubscribeLocalEvent<ST14EnergyBoxComponent, EntInsertedIntoContainerMessage>(OnBlockInserted);
        SubscribeLocalEvent<ST14EnergyBoxComponent, EntRemovedFromContainerMessage>(OnBlockRemoved);
        SubscribeLocalEvent<ST14EnergyBoxComponent, ActivateInWorldEvent>(OnBoxActivate);
        SubscribeLocalEvent<ST14EnergyBoxComponent, GetVerbsEvent<AlternativeVerb>>(OnBoxVerbs);
        SubscribeLocalEvent<ST14EnergyBoxComponent, ExaminedEvent>(OnBoxExamined);

        SubscribeLocalEvent<ST14EnergyBlockComponent, ChargeChangedEvent>(OnBlockChargeChanged);
        SubscribeLocalEvent<ST14EnergyBlockComponent, MapInitEvent>(OnBlockMapInit);
    }

    /// <summary>
    /// Energy block in the panel, if any
    /// </summary>
    public EntityUid? GetBlock(Entity<ST14EnergyBoxComponent> box)
    {
        return _slots.GetItemOrNull(box.Owner, box.Comp.BlockSlot);
    }

    public void SetBreaker(Entity<ST14EnergyBoxComponent> box, bool on, EntityUid? user = null)
    {
        if (box.Comp.Breaker == on)
            return;

        box.Comp.Breaker = on;
        _audio.PlayPvs(box.Comp.BreakerSound, box);
        UpdateVisuals(box, force: true);
    }

    private void OnBoxMapInit(Entity<ST14EnergyBoxComponent> ent, ref MapInitEvent args)
    {
        TakeBlock(ent);
        UpdateVisuals(ent, force: true);
    }

    private void OnBlockInserted(Entity<ST14EnergyBoxComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.BlockSlot)
            return;

        TakeBlock(ent);
        UpdateVisuals(ent, force: true);
    }

    private void OnBlockRemoved(Entity<ST14EnergyBoxComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.BlockSlot)
            return;

        if (ent.Comp.Block == args.Entity)
            Flush(ent);

        ent.Comp.Block = null;
        ent.Comp.Charge = 0f;
        UpdateVisuals(ent, force: true);
    }

    private void TakeBlock(Entity<ST14EnergyBoxComponent> ent)
    {
        ent.Comp.Block = GetBlock(ent);
        ent.Comp.Charge = ent.Comp.Block is { } block ? _battery.GetCharge(block).Charge : 0f;
    }

    /// <summary>
    /// Writes the panel's copy of the charge back into the block
    /// </summary>
    private void Flush(Entity<ST14EnergyBoxComponent> ent)
    {
        if (ent.Comp.Block is not { } block || TerminatingOrDeleted(block))
            return;

        var (charge, max) = _battery.GetCharge(block);
        var value = Math.Clamp(ent.Comp.Charge, 0f, max);
        if (!MathHelper.CloseTo(charge, value))
            _battery.SetCharge(block, value);
    }

    private void OnBoxActivate(Entity<ST14EnergyBoxComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        SetBreaker(ent, !ent.Comp.Breaker, args.User);
    }

    private void OnBoxVerbs(Entity<ST14EnergyBoxComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.Breaker ? "st14-energy-box-verb-breaker-off" : "st14-energy-box-verb-breaker-on"),
            Priority = 2,
            Act = () => SetBreaker(ent, !ent.Comp.Breaker, user),
        });
    }

    private void OnBoxExamined(Entity<ST14EnergyBoxComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.Breaker ? "st14-energy-box-examine-breaker-on" : "st14-energy-box-examine-breaker-off"));

        if (ent.Comp.Block is not { } block || !TryComp<BatteryComponent>(block, out var battery) || battery.MaxCharge <= 0f)
        {
            args.PushMarkup(Loc.GetString("st14-energy-box-examine-empty"));
            return;
        }

        var percent = (int) MathF.Round(ent.Comp.Charge / battery.MaxCharge * 100f);
        args.PushMarkup(Loc.GetString("st14-energy-box-examine-charge", ("percent", percent)));
    }

    private void OnBlockChargeChanged(Entity<ST14EnergyBlockComponent> ent, ref ChargeChangedEvent args)
    {
        SetBlockLevel(ent, args.MaxCharge > 0f ? args.CurrentCharge / args.MaxCharge : 0f);
    }

    private void OnBlockMapInit(Entity<ST14EnergyBlockComponent> ent, ref MapInitEvent args)
    {
        SetBlockLevel(ent, _battery.GetChargeLevel(ent.Owner));
    }

    private void SetBlockLevel(EntityUid block, float fraction)
    {
        _appearance.SetData(block, ST14EnergyBlockVisuals.Level, GetLevel(fraction));
    }

    /// <summary>
    /// 0 for an empty block, 4 for a full one; any charge at all shows at least 1
    /// </summary>
    public static int GetLevel(float fraction)
    {
        if (fraction <= 0.001f)
            return 0;

        return Math.Clamp((int) MathF.Ceiling(fraction * (ChargeLevels - 1)), 1, ChargeLevels - 1);
    }

    private void OnPreSync(NetworkBatteryPreSync ev)
    {
        var query = EntityQueryEnumerator<ST14EnergyBoxComponent, PowerNetworkBatteryComponent, TransformComponent>();
        while (query.MoveNext(out _, out var box, out var netBattery, out var xform))
        {
            var battery = netBattery.NetworkBattery;
            if (box.Block is not { } block
                || !xform.Anchored
                || !TryComp<BatteryComponent>(block, out var blockBattery)
                || !TryComp<ST14EnergyBlockComponent>(block, out var blockComp))
            {
                // Without a block the panel is a gap in the wire
                battery.Enabled = false;
                battery.Capacity = 0f;
                battery.CurrentStorage = 0f;
                continue;
            }

            battery.Enabled = true;
            battery.CanCharge = true;
            battery.CanDischarge = box.Breaker;
            battery.MaxSupply = blockComp.MaxOutput;
            battery.MaxChargeRate = blockComp.MaxChargeRate;
            battery.Capacity = blockBattery.MaxCharge;
            battery.CurrentStorage = Math.Clamp(box.Charge, 0f, blockBattery.MaxCharge);
        }
    }

    private void OnPostSync(NetworkBatteryPostSync ev)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ST14EnergyBoxComponent, PowerNetworkBatteryComponent>();
        while (query.MoveNext(out var uid, out var box, out var netBattery))
        {
            if (Paused(uid))
                continue;

            var battery = netBattery.NetworkBattery;
            if (box.Block == null || !battery.Enabled)
            {
                if (now >= box.NextVisuals)
                    UpdateVisuals((uid, box));
                continue;
            }

            box.Charge = battery.CurrentStorage;

            if (now >= box.NextFlush)
            {
                box.NextFlush = now + FlushDelay;
                Flush((uid, box));
            }

            if (now >= box.NextVisuals)
                UpdateVisuals((uid, box));
        }
    }

    private void UpdateVisuals(Entity<ST14EnergyBoxComponent> ent, bool force = false)
    {
        ent.Comp.NextVisuals = _timing.CurTime + VisualsDelay;

        var hasBlock = ent.Comp.Block != null;
        var level = 0;
        var discharging = false;

        if (ent.Comp.Block is { } block && TryComp<BatteryComponent>(block, out var blockBattery) && blockBattery.MaxCharge > 0f)
            level = GetLevel(ent.Comp.Charge / blockBattery.MaxCharge);

        if (hasBlock && TryComp<PowerNetworkBatteryComponent>(ent, out var netBattery))
        {
            var battery = netBattery.NetworkBattery;
            discharging = battery.Enabled && battery.CurrentSupply > battery.CurrentReceiving * battery.Efficiency + 1f;
        }

        _appearance.SetData(ent, ST14EnergyBoxVisuals.Breaker, ent.Comp.Breaker);
        _appearance.SetData(ent, ST14EnergyBoxVisuals.HasBlock, hasBlock);
        _appearance.SetData(ent, ST14EnergyBoxVisuals.ChargeLevel, level);
        _appearance.SetData(ent, ST14EnergyBoxVisuals.Discharging, discharging);
    }
}
