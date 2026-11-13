using System.Text;
using Content.Server._ST14.Sectors;
using Content.Server.Radio.EntitySystems;
using Content.Shared._ST14.Comms;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._ST14.Comms;

/// <summary>
/// The radio of the troopers. The squad hears each other where the radio pack of a signaller of the squad carries.
/// The Rodger Young, the channel of every squad on the planet, needs a comms tower holding a frequency, which a
/// signaller searches on the tuner of the tower, and a radio pack set to that frequency to carry it. The frequency
/// drifts away: the words break up and at last the channel is silent until a signaller catches a new one
/// </summary>
public sealed partial class ST14CommsSystem : EntitySystem
{
    private const string BackSlot = "back";

    /// <summary>
    /// How far from the hidden frequency the tuner still hears something, in tenths of a megahertz
    /// </summary>
    private const float Hearing = 150f;

    /// <summary>
    /// How near the dial has to stand to the hidden frequency to catch it, in tenths of a megahertz
    /// </summary>
    private const int CatchWindow = 2;

    private const float TunerRange = 2.5f;
    private static readonly TimeSpan TunerUpdate = TimeSpan.FromSeconds(0.1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ST14BattleMapSystem _battleMap = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    /// <summary>
    /// The tower speaks itself: its words reach everyone on the channel
    /// </summary>
    private bool _towerSpeaks;

    private TimeSpan _nextTunerUpdate;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14SquadRadioComponent, EntitySpokeEvent>(OnSpoke);
        SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnReceiveAttempt);

        SubscribeLocalEvent<ST14RadioPackComponent, GotEquippedEvent>(OnPackEquipped);
        SubscribeLocalEvent<ST14RadioPackComponent, GotUnequippedEvent>(OnPackUnequipped);
        SubscribeLocalEvent<ST14RadioPackComponent, GetVerbsEvent<AlternativeVerb>>(OnPackVerbs);
        SubscribeLocalEvent<ST14RadioPackComponent, ST14TunerDialMessage>(OnPackDial);
        SubscribeLocalEvent<ST14RadioPackComponent, ExaminedEvent>(OnPackExamined);
        SubscribeLocalEvent<ST14RadioPackComponent, ST14RadioPackPowerMessage>(OnPackPower);
        SubscribeLocalEvent<ST14RadioPackComponent, ST14RadioPackRangeMessage>(OnPackRange);
        SubscribeLocalEvent<ST14RadioPackComponent, ST14RadioPackPresetMessage>(OnPackPreset);

        SubscribeLocalEvent<ST14CommsTowerComponent, ActivateInWorldEvent>(OnTowerActivate);
        SubscribeLocalEvent<ST14CommsTowerComponent, ST14TunerDialMessage>(OnTowerDial);
        SubscribeLocalEvent<ST14CommsTowerComponent, BoundUIClosedEvent>(OnTunerClosed);
        SubscribeLocalEvent<ST14CommsTowerComponent, ExaminedEvent>(OnTowerExamined);
    }

    #region Radio pack

    private void OnPackEquipped(EntityUid uid, ST14RadioPackComponent comp, GotEquippedEvent args)
    {
        if (args.Slot != BackSlot)
            return;

        comp.Wearer = args.EquipTarget;
        Dirty(uid, comp);
        RefreshPack((uid, comp));
    }

    private void OnPackUnequipped(EntityUid uid, ST14RadioPackComponent comp, GotUnequippedEvent args)
    {
        if (comp.Wearer == null)
            return;

        comp.Wearer = null;
        Dirty(uid, comp);
        RefreshPack((uid, comp));
    }

    private void OnPackVerbs(EntityUid uid, ST14RadioPackComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("st14-radio-pack-verb-tune"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            Priority = 2,
            Act = () =>
            {
                if (!HasComp<ST14SignallerComponent>(user))
                {
                    _popup.PopupEntity(Loc.GetString("st14-radio-pack-signaller-only"), uid, user);
                    return;
                }

                _ui.OpenUi(uid, ST14TunerUiKey.Key, user);
                UpdatePackTuner((uid, comp));
            },
        });
    }

    private void OnPackDial(EntityUid uid, ST14RadioPackComponent comp, ST14TunerDialMessage args)
    {
        if (!HasComp<ST14SignallerComponent>(args.Actor))
            return;

        comp.Frequency = Math.Clamp(args.Dial, ST14Frequency.Min, ST14Frequency.Max);
        Dirty(uid, comp);
        UpdatePackTuner((uid, comp));
    }

    private void OnPackPower(EntityUid uid, ST14RadioPackComponent comp, ST14RadioPackPowerMessage args)
    {
        if (!HasComp<ST14SignallerComponent>(args.Actor))
            return;

        comp.Powered = args.Powered;
        Dirty(uid, comp);
        RefreshPack((uid, comp));
        UpdatePackTuner((uid, comp));
    }

    private void OnPackRange(EntityUid uid, ST14RadioPackComponent comp, ST14RadioPackRangeMessage args)
    {
        if (!HasComp<ST14SignallerComponent>(args.Actor))
            return;

        comp.Power = Math.Clamp(args.Power, 0, comp.Ranges.Length - 1);
        Dirty(uid, comp);
        RefreshPack((uid, comp));
        UpdatePackTuner((uid, comp));
    }

    private void OnPackPreset(EntityUid uid, ST14RadioPackComponent comp, ST14RadioPackPresetMessage args)
    {
        if (!HasComp<ST14SignallerComponent>(args.Actor) || args.Slot < 0 || args.Slot >= comp.Presets.Length)
            return;

        if (args.Save)
            comp.Presets[args.Slot] = comp.Frequency;
        else
            comp.Frequency = comp.Presets[args.Slot];

        Dirty(uid, comp);
        UpdatePackTuner((uid, comp));
    }

    /// <summary>
    /// The light of the pack. A strong transmitter is not seen by the bugs, whatever its power
    /// </summary>
    private void RefreshPack(Entity<ST14RadioPackComponent> ent)
    {
        var working = ent.Comp.Wearer != null && ent.Comp.Powered;
        _appearance.SetData(ent, ST14RadioPackVisuals.Active, working);
    }

    private void OnPackExamined(EntityUid uid, ST14RadioPackComponent comp, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("st14-radio-pack-examine", ("frequency", ST14Frequency.Format(comp.Frequency))));
    }

    private void UpdatePackTuner(Entity<ST14RadioPackComponent> ent)
    {
        var tower = LiveTower(ent.Comp.Frequency);
        var signal = 0f;
        var query = EntityQueryEnumerator<ST14CommsTowerComponent>();
        while (query.MoveNext(out var comp))
        {
            if (Quality(comp) > 0f)
                signal = MathF.Max(signal, Signal(ent.Comp.Frequency, comp.Frequency));
        }

        // on a pack "lock" says it stands on the frequency of a tower
        var on = ent.Comp.Powered;
        var state = new ST14TunerState(true, ent.Comp.Frequency, on ? signal : 0f, on && tower != null ? 1f : 0f,
            tower is { } held ? Quality(held) : 0f, null, on, ent.Comp.Power, ent.Comp.Range, ent.Comp.Presets);
        _ui.SetUiState(ent.Owner, ST14TunerUiKey.Key, state);
    }

    /// <summary>
    /// The living one who wears the pack on the back, if it works now
    /// </summary>
    private EntityUid? WorkingWearer(EntityUid pack, ST14RadioPackComponent comp)
    {
        if (!comp.Powered ||
            comp.Wearer is not { } wearer ||
            TerminatingOrDeleted(wearer) ||
            Transform(pack).ParentUid != wearer ||
            _mobState.IsDead(wearer))
        {
            return null;
        }

        return wearer;
    }

    /// <summary>
    /// A radio pack worn by a signaller of the squad carries to the body
    /// </summary>
    public bool SquadCovers(int squad, EntityUid body)
    {
        var where = _transform.GetMapCoordinates(body);
        var query = EntityQueryEnumerator<ST14RadioPackComponent>();
        while (query.MoveNext(out var pack, out var comp))
        {
            if (WorkingWearer(pack, comp) is { } wearer &&
                _battleMap.SquadIdOf(wearer) == squad &&
                _transform.GetMapCoordinates(wearer).InRange(where, comp.Range))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Radio

    private void OnSpoke(EntityUid uid, ST14SquadRadioComponent comp, EntitySpokeEvent args)
    {
        if (args.Channel is not { } channel)
            return;

        if (channel.ID == ST14SquadRadioComponent.SquadChannel)
        {
            args.Channel = null;
            if (_battleMap.SquadIdOf(uid) is not { } squad)
            {
                _popup.PopupEntity(Loc.GetString("st14-radio-no-squad"), uid, uid);
                return;
            }

            if (!SquadCovers(squad, uid))
            {
                _popup.PopupEntity(Loc.GetString("st14-radio-squad-no-signal"), uid, uid);
                return;
            }

            _radio.SendRadioMessage(uid, args.Message, channel, uid);
        }
        else if (channel.ID == ST14SquadRadioComponent.RodgerChannel)
        {
            args.Channel = null;
            var quality = RodgerQuality();
            if (quality <= 0f)
            {
                _popup.PopupEntity(Loc.GetString("st14-radio-rodger-no-tower"), uid, uid);
                return;
            }

            if (!ReachesRodger(uid))
            {
                _popup.PopupEntity(Loc.GetString("st14-radio-rodger-no-signal"), uid, uid);
                return;
            }

            _radio.SendRadioMessage(uid, Garble(args.Message, quality), channel, uid);
        }
    }

    private void OnReceiveAttempt(ref RadioReceiveAttemptEvent args)
    {
        if (_towerSpeaks)
            return;

        if (args.Channel.ID == ST14SquadRadioComponent.SquadChannel)
        {
            var squad = _battleMap.SquadIdOf(args.RadioSource);
            if (squad is not { } id || _battleMap.SquadIdOf(args.RadioReceiver) != id || !SquadCovers(id, args.RadioReceiver))
                args.Cancelled = true;
        }
        else if (args.Channel.ID == ST14SquadRadioComponent.RodgerChannel)
        {
            if (!ReachesRodger(args.RadioReceiver))
                args.Cancelled = true;
        }
    }

    /// <summary>
    /// The body reaches the Rodger Young: a radio pack set to the frequency of a tower carries to it, or it stands by
    /// a tower that holds one
    /// </summary>
    private bool ReachesRodger(EntityUid body)
    {
        var where = _transform.GetMapCoordinates(body);
        var packs = EntityQueryEnumerator<ST14RadioPackComponent>();
        while (packs.MoveNext(out var pack, out var comp))
        {
            if (WorkingWearer(pack, comp) is { } wearer &&
                LiveTower(comp.Frequency) != null &&
                _transform.GetMapCoordinates(wearer).InRange(where, comp.Range))
            {
                return true;
            }
        }

        var towers = EntityQueryEnumerator<ST14CommsTowerComponent>();
        while (towers.MoveNext(out var tower, out var comp))
        {
            if (Quality(comp) > 0f && _transform.GetMapCoordinates(tower).InRange(where, comp.Range))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A tower that holds this frequency now
    /// </summary>
    private ST14CommsTowerComponent? LiveTower(int frequency)
    {
        var query = EntityQueryEnumerator<ST14CommsTowerComponent>();
        while (query.MoveNext(out var comp))
        {
            if (comp.Frequency == frequency && Quality(comp) > 0f)
                return comp;
        }

        return null;
    }

    /// <summary>
    /// How clean the best tower of the planet holds its frequency, 0 when none does
    /// </summary>
    public float RodgerQuality()
    {
        var best = 0f;
        var query = EntityQueryEnumerator<ST14CommsTowerComponent>();
        while (query.MoveNext(out var comp))
        {
            best = MathF.Max(best, Quality(comp));
        }

        return best;
    }

    private float Quality(ST14CommsTowerComponent tower)
    {
        if (tower.CaughtAt is not { } caught)
            return 0f;

        var held = _timing.CurTime - caught;
        if (held < tower.HoldTime)
            return 1f;

        return Math.Clamp(1f - (float) ((held - tower.HoldTime) / tower.FadeTime), 0f, 1f);
    }

    /// <summary>
    /// A weak signal breaks the words up: letters go to static
    /// </summary>
    private string Garble(string message, float quality)
    {
        if (quality >= 1f)
            return message;

        var chance = (1f - quality) * 0.85f;
        var text = new StringBuilder(message.Length);
        foreach (var c in message)
        {
            text.Append(char.IsLetterOrDigit(c) && _random.Prob(chance) ? _random.Pick(Static) : c);
        }

        return text.ToString();
    }

    private static readonly char[] Static = { 'ш', 'ш', 'ш', '-', '~' };

    private void TowerSays(EntityUid tower, string message)
    {
        if (!_prototype.TryIndex(ST14SquadRadioComponent.RodgerChannel, out var channel))
            return;

        _towerSpeaks = true;
        try
        {
            _radio.SendRadioMessage(tower, message, channel, tower);
        }
        finally
        {
            _towerSpeaks = false;
        }
    }

    #endregion

    #region Tower

    private void OnTowerExamined(EntityUid uid, ST14CommsTowerComponent comp, ExaminedEvent args)
    {
        var quality = Quality(comp);
        args.PushMarkup(quality > 0f
            ? Loc.GetString("st14-tower-examine-held", ("frequency", ST14Frequency.Format(comp.Frequency)),
                ("percent", (int) MathF.Round(quality * 100f)))
            : Loc.GetString("st14-tower-examine-none"));
    }

    private void OnTowerActivate(EntityUid uid, ST14CommsTowerComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        var user = args.User;

        if (!HasComp<ST14SignallerComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("st14-tower-signaller-only"), uid, user);
            return;
        }

        if (!_inventory.TryGetSlotEntity(user, BackSlot, out var pack) || !TryComp<ST14RadioPackComponent>(pack, out var packComp))
        {
            _popup.PopupEntity(Loc.GetString("st14-tower-no-pack"), uid, user);
            return;
        }

        if (comp.Tuner is { } other && other != user && Exists(other))
        {
            _popup.PopupEntity(Loc.GetString("st14-tower-busy"), uid, user);
            return;
        }

        // a new frequency hidden somewhere on the scale, the dial starts where the pack stands
        comp.Tuner = user;
        comp.Target = _random.Next(ST14Frequency.Min + 20, ST14Frequency.Max - 20);
        comp.Dial = packComp.Frequency;
        comp.Lock = 0f;

        _ui.OpenUi(uid, ST14TunerUiKey.Key, user);
        UpdateTowerTuner((uid, comp));
    }

    private void OnTowerDial(EntityUid uid, ST14CommsTowerComponent comp, ST14TunerDialMessage args)
    {
        if (args.Actor != comp.Tuner)
            return;

        comp.Dial = Math.Clamp(args.Dial, ST14Frequency.Min, ST14Frequency.Max);
        UpdateTowerTuner((uid, comp));
    }

    private void OnTunerClosed(EntityUid uid, ST14CommsTowerComponent comp, BoundUIClosedEvent args)
    {
        if (args.Actor == comp.Tuner)
            StopTuning((uid, comp), false);
    }

    private void StopTuning(Entity<ST14CommsTowerComponent> ent, bool closeUi)
    {
        var tuner = ent.Comp.Tuner;
        ent.Comp.Tuner = null;
        ent.Comp.Lock = 0f;
        if (closeUi && tuner != null)
            _ui.CloseUi(ent.Owner, ST14TunerUiKey.Key, tuner);
    }

    private static float Signal(int dial, int frequency)
    {
        return Math.Clamp(1f - MathF.Abs(dial - frequency) / Hearing, 0f, 1f);
    }

    /// <summary>
    /// The search is over once the frequency was caught: the target is cleared, the tuner shows the caught one
    /// </summary>
    private static bool Searching(ST14CommsTowerComponent tower)
    {
        return tower.Target > 0;
    }

    private void UpdateTowerTuner(Entity<ST14CommsTowerComponent> ent)
    {
        var comp = ent.Comp;
        var quality = Quality(comp);
        var state = Searching(comp)
            ? new ST14TunerState(false, comp.Dial, Signal(comp.Dial, comp.Target), comp.Lock / comp.LockTime, quality,
                quality > 0f ? comp.Frequency : null)
            : new ST14TunerState(false, comp.Dial, 1f, 1f, quality, quality > 0f ? comp.Frequency : null);
        _ui.SetUiState(ent.Owner, ST14TunerUiKey.Key, state);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var sendUi = _timing.CurTime >= _nextTunerUpdate;
        if (sendUi)
            _nextTunerUpdate = _timing.CurTime + TunerUpdate;

        var query = EntityQueryEnumerator<ST14CommsTowerComponent>();
        while (query.MoveNext(out var uid, out var tower))
        {
            var ent = new Entity<ST14CommsTowerComponent>(uid, tower);
            var quality = Quality(tower);
            _appearance.SetData(uid, ST14CommsTowerVisuals.Online, quality > 0f);

            if (tower.CaughtAt is { } caught)
            {
                var frequency = ST14Frequency.Format(tower.Frequency);
                if (!tower.WarnedFade && _timing.CurTime - caught >= tower.HoldTime)
                {
                    tower.WarnedFade = true;
                    TowerSays(uid, Loc.GetString("st14-tower-says-fading", ("frequency", frequency)));
                }

                if (!tower.WarnedLost && quality <= 0f)
                {
                    tower.WarnedLost = true;
                    tower.CaughtAt = null;
                    TowerSays(uid, Loc.GetString("st14-tower-says-lost", ("frequency", frequency)));
                }
            }

            if (tower.Tuner is not { } tuner)
                continue;

            if (!Exists(tuner) ||
                _mobState.IsIncapacitated(tuner) ||
                !_transform.GetMapCoordinates(tuner).InRange(_transform.GetMapCoordinates(uid), TunerRange))
            {
                StopTuning(ent, true);
                continue;
            }

            if (Searching(tower))
            {
                tower.Lock = Math.Abs(tower.Dial - tower.Target) <= CatchWindow
                    ? tower.Lock + frameTime
                    : MathF.Max(0f, tower.Lock - frameTime * 2f);

                if (tower.Lock >= tower.LockTime)
                {
                    tower.Frequency = tower.Target;
                    tower.Target = 0;
                    tower.CaughtAt = _timing.CurTime;
                    tower.WarnedFade = false;
                    tower.WarnedLost = false;

                    var frequency = ST14Frequency.Format(tower.Frequency);
                    _popup.PopupEntity(Loc.GetString("st14-tower-caught", ("frequency", frequency)), uid, tuner, PopupType.Medium);
                    TowerSays(uid, Loc.GetString("st14-tower-says-caught", ("frequency", frequency)));
                    UpdateTowerTuner(ent);
                    continue;
                }
            }

            if (sendUi)
                UpdateTowerTuner(ent);
        }

        if (!sendUi)
            return;

        // the tuners of the packs follow the towers: the signal fades, a new frequency is caught
        var packs = EntityQueryEnumerator<ST14RadioPackComponent>();
        while (packs.MoveNext(out var pack, out var comp))
        {
            if (_ui.IsUiOpen(pack, ST14TunerUiKey.Key))
                UpdatePackTuner((pack, comp));
        }
    }

    #endregion
}
