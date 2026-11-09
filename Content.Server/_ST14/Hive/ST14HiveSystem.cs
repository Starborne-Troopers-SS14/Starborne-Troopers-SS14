using System.Linq;
using Content.Shared._ST14.Hive;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Hive;

/// <summary>
/// The hive as a whole: shared hive points and what the brain bug has opened, personal evolution points of every bug,
/// the evolution menu and the evolution itself
/// </summary>
public sealed partial class ST14HiveSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    /// <summary>
    /// Hive points the brain bug has to spend
    /// </summary>
    public int HivePoints { get; private set; }

    private readonly HashSet<ST14HiveTab> _openTabs = new() { ST14HiveTab.T1 };
    private readonly HashSet<ProtoId<ST14HiveCastePrototype>> _openSpecies = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeLocalEvent<ST14HiveMemberComponent, MapInitEvent>(OnMemberMapInit);
        SubscribeLocalEvent<ST14HiveMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<ST14HiveMemberComponent, ST14HiveEvolveActionEvent>(OnEvolveAction);
        SubscribeLocalEvent<ST14HiveMemberComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ST14HiveMemberComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ST14HiveMemberComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ST14HiveMemberComponent, ST14HiveEvolveMessage>(OnEvolveMessage);
        SubscribeLocalEvent<ST14HiveMemberComponent, ST14HiveOpenTabMessage>(OnOpenTabMessage);
        SubscribeLocalEvent<ST14HiveMemberComponent, ST14HiveOpenSpeciesMessage>(OnOpenSpeciesMessage);
        SubscribeLocalEvent<ST14HiveMemberComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        HivePoints = 0;
        _openTabs.Clear();
        _openTabs.Add(ST14HiveTab.T1);
        _openSpecies.Clear();
    }

    #region Points

    /// <summary>
    /// Gives a bug personal evolution points
    /// </summary>
    public void AddEvolutionPoints(Entity<ST14HiveMemberComponent?> ent, float points)
    {
        if (points <= 0 || !Resolve(ent, ref ent.Comp, false))
            return;

        var before = (int) ent.Comp.EvolutionPoints;
        ent.Comp.EvolutionPoints += points;

        if ((int) ent.Comp.EvolutionPoints != before)
            UpdateUi(ent);
    }

    /// <summary>
    /// Gives the whole hive points, for the brain bug to spend
    /// </summary>
    public void AddHivePoints(int points)
    {
        if (points <= 0)
            return;

        HivePoints += points;
        UpdateAllUis();
    }

    /// <summary>
    /// How much a sucked brain is worth with the current number of players online
    /// </summary>
    public float GetOnlineMultiplier(int players)
    {
        foreach (var step in _prototype.Index(ST14HiveSettingsPrototype.Default).OnlineMultipliers)
        {
            if (step.MaxPlayers == null || players <= step.MaxPlayers)
                return step.Multiplier;
        }

        return 1f;
    }

    private void OnMeleeHit(Entity<ST14HiveMemberComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0)
            return;

        var damage = 0f;
        foreach (var hit in args.HitEntities)
        {
            if (hit == ent.Owner || HasComp<ST14HiveMemberComponent>(hit) || HasComp<ST14HiveQueenComponent>(hit))
                continue;

            damage += args.BaseDamage.GetTotal().Float();
        }

        AddEvolutionPoints(ent.AsNullable(), damage * ent.Comp.PointsPerDamage);
    }

    #endregion

    #region Member

    private void OnMemberMapInit(Entity<ST14HiveMemberComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.CanEvolve)
            _actions.AddAction(ent, ref ent.Comp.EvolveActionEntity, ent.Comp.EvolveAction);
    }

    private void OnMemberShutdown(Entity<ST14HiveMemberComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.EvolveActionEntity);
    }

    private void OnEvolveAction(Entity<ST14HiveMemberComponent> ent, ref ST14HiveEvolveActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.TryToggleUi(ent.Owner, ST14HiveEvolutionUiKey.Key, ent.Owner);
    }

    private void OnMobStateChanged(Entity<ST14HiveMemberComponent> ent, ref MobStateChangedEvent args)
    {
        // The damage overlays are drawn for the four directions of a live bug and do not fit the corpse
        _appearance.SetData(ent, DamageVisualizerKeys.Disabled, args.NewMobState == MobState.Dead);

        if (args.NewMobState == MobState.Dead && ent.Comp.DeathSound != null)
            _audio.PlayPvs(ent.Comp.DeathSound, ent);

        if (args.NewMobState != MobState.Alive)
            _ui.CloseUi(ent.Owner, ST14HiveEvolutionUiKey.Key);
    }

    private void OnDamageChanged(Entity<ST14HiveMemberComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || ent.Comp.PainSound == null || _timing.CurTime < ent.Comp.NextPain)
            return;

        ent.Comp.NextPain = _timing.CurTime + ent.Comp.PainCooldown;
        _audio.PlayPvs(ent.Comp.PainSound, ent);
    }

    #endregion

    #region Menu

    private void OnUiOpened(Entity<ST14HiveMemberComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent.AsNullable());
    }

    private void OnEvolveMessage(Entity<ST14HiveMemberComponent> ent, ref ST14HiveEvolveMessage args)
    {
        if (!_prototype.Resolve(args.Caste, out var caste) || caste.Entity == null)
            return;

        if (GetStatus(ent, caste) != ST14CasteStatus.Available)
        {
            UpdateUi(ent.AsNullable());
            return;
        }

        Evolve(ent, caste);
    }

    private void OnOpenTabMessage(Entity<ST14HiveMemberComponent> ent, ref ST14HiveOpenTabMessage args)
    {
        if (!HasComp<ST14BrainBugComponent>(ent) || _openTabs.Contains(args.Tab))
            return;

        // Opening a tier with nothing in it would only burn the points
        var tab = args.Tab;
        if (!_prototype.EnumeratePrototypes<ST14HiveCastePrototype>().Any(c => c.Tab == tab && c.Entity != null))
            return;

        if (!_prototype.Index(ST14HiveSettingsPrototype.Default).TabCosts.TryGetValue(args.Tab, out var cost))
            return;

        if (HivePoints < cost)
        {
            _popup.PopupEntity(Loc.GetString("st14-hive-not-enough-hive-points"), ent, ent);
            return;
        }

        HivePoints -= cost;
        _openTabs.Add(args.Tab);
        UpdateAllUis();
    }

    private void OnOpenSpeciesMessage(Entity<ST14HiveMemberComponent> ent, ref ST14HiveOpenSpeciesMessage args)
    {
        if (!HasComp<ST14BrainBugComponent>(ent) ||
            _openSpecies.Contains(args.Caste) ||
            !_prototype.Resolve(args.Caste, out var caste) ||
            caste.HiveCost <= 0 ||
            !_openTabs.Contains(caste.Tab))
            return;

        if (HivePoints < caste.HiveCost)
        {
            _popup.PopupEntity(Loc.GetString("st14-hive-not-enough-hive-points"), ent, ent);
            return;
        }

        HivePoints -= caste.HiveCost;
        _openSpecies.Add(args.Caste);
        UpdateAllUis();
    }

    /// <summary>
    /// Whether this bug can become the given caste right now, and if not, why
    /// </summary>
    public ST14CasteStatus GetStatus(Entity<ST14HiveMemberComponent> ent, ST14HiveCastePrototype caste)
    {
        if (ent.Comp.Caste == caste.ID)
            return ST14CasteStatus.Current;

        if (!_openTabs.Contains(caste.Tab))
            return ST14CasteStatus.TabClosed;

        if (caste.HiveCost > 0 && !_openSpecies.Contains(caste.ID))
            return ST14CasteStatus.SpeciesClosed;

        if (TryComp<ST14BugLarvaComponent>(ent, out var larva))
        {
            if (!caste.FromLarva)
                return ST14CasteStatus.Unreachable;

            return larva.Biomass < larva.BiomassNeeded ? ST14CasteStatus.NotEnoughBiomass : ST14CasteStatus.Available;
        }

        if (ent.Comp.Caste == null || !caste.EvolvesFrom.Contains(ent.Comp.Caste.Value))
            return ST14CasteStatus.Unreachable;

        return ent.Comp.EvolutionPoints < caste.Cost ? ST14CasteStatus.NotEnoughPoints : ST14CasteStatus.Available;
    }

    public void UpdateUi(Entity<ST14HiveMemberComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !_ui.IsUiOpen(ent.Owner, ST14HiveEvolutionUiKey.Key))
            return;

        var member = new Entity<ST14HiveMemberComponent>(ent, ent.Comp);
        var entries = new List<ST14CasteEntry>();
        foreach (var caste in _prototype.EnumeratePrototypes<ST14HiveCastePrototype>())
        {
            if (caste.Entity != null)
                entries.Add(new ST14CasteEntry(caste.ID, GetStatus(member, caste)));
        }

        int? biomass = null;
        var needed = 0;
        if (TryComp<ST14BugLarvaComponent>(ent, out var larva))
        {
            biomass = larva.Biomass;
            needed = larva.BiomassNeeded;
        }

        var state = new ST14HiveEvolutionState(
            entries,
            (int) ent.Comp.EvolutionPoints,
            HivePoints,
            new HashSet<ST14HiveTab>(_openTabs),
            new HashSet<ProtoId<ST14HiveCastePrototype>>(_openSpecies),
            HasComp<ST14BrainBugComponent>(ent),
            HasComp<ST14BugLarvaComponent>(ent),
            biomass,
            needed);

        _ui.SetUiState(ent.Owner, ST14HiveEvolutionUiKey.Key, state);
    }

    private void UpdateAllUis()
    {
        var query = EntityQueryEnumerator<ST14HiveMemberComponent>();
        while (query.MoveNext(out var uid, out var member))
        {
            UpdateUi((uid, member));
        }
    }

    #endregion

    #region Evolution

    /// <summary>
    /// Turns the bug into a new caste: a new body takes the mind and the leftover evolution points, the old one is gone
    /// </summary>
    public EntityUid? Evolve(Entity<ST14HiveMemberComponent> ent, ST14HiveCastePrototype caste)
    {
        if (caste.Entity == null)
            return null;

        var xform = Transform(ent);
        var coords = _transform.GetMapCoordinates(ent, xform);
        var child = Spawn(caste.Entity.Value, coords, rotation: _transform.GetWorldRotation(ent));

        var points = ent.Comp.EvolutionPoints;
        if (!HasComp<ST14BugLarvaComponent>(ent))
            points -= caste.Cost;

        var childMember = EnsureComp<ST14HiveMemberComponent>(child);
        childMember.Caste = caste.ID;
        childMember.EvolutionPoints = Math.Max(0, points);

        _ui.CloseUi(ent.Owner, ST14HiveEvolutionUiKey.Key);

        if (_mind.TryGetMind(ent, out var mindId, out var mind))
            _mind.TransferTo(mindId, child, mind: mind);

        if (childMember.EvolveSound != null)
            _audio.PlayPvs(childMember.EvolveSound, child);

        _popup.PopupEntity(Loc.GetString("st14-hive-evolved", ("caste", Loc.GetString(caste.Name))), child, PopupType.Medium);
        QueueDel(ent);
        return child;
    }

    #endregion
}
