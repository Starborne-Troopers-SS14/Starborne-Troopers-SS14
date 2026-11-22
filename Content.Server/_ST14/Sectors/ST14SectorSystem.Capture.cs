using System.Linq;
using Content.Shared._ST14.MainHub;
using Content.Shared._ST14.Sectors;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.GameTicking;

namespace Content.Server._ST14.Sectors;

/// <summary>
/// How a battle ends. It has no time limit: it lasts until a sector of the front - the target or the sector the attacker
/// gathers in - is taken. A side takes a sector of the front it does not hold when its buildings stand on every
/// resource point of it and the enemy is driven out of it (fewer than <see cref="ST14SectorSettingsPrototype.DrivenOutBelow"/>
/// live fighters on all its levels), and both stay so for <see cref="ST14SectorSettingsPrototype.HoldTime"/>.
/// A broken hold starts over. When both sides hold a sector of the other at once, both clocks stop until one of the holds
/// breaks. Only the two sectors of the front count: the rear of either side never keeps a battle going
/// </summary>
public sealed partial class ST14SectorSystem
{
    /// <summary>
    /// How long each sector of the front has been held to be taken, by the side holding it
    /// </summary>
    private readonly Dictionary<int, (ST14GalaxyFaction Side, TimeSpan Held)> _holds = new();

    private TimeSpan _lastCheck;
    private bool _paused;

    private void ResetCapture()
    {
        _holds.Clear();
        _paused = false;
    }

    /// <summary>
    /// The sectors whose fate decides the battle: the target, and the sector the attacker gathers in
    /// </summary>
    public IEnumerable<int> FrontCells()
    {
        if (_target >= 0 && _maps.ContainsKey(_target))
            yield return _target;

        if (_staging >= 0 && _staging != _target && _maps.ContainsKey(_staging))
            yield return _staging;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_planet is not { } planet || _decided || _ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextCheck)
            return;

        var now = _timing.CurTime;
        var elapsed = now - _lastCheck;
        _lastCheck = now;
        _nextCheck = now + Settings.CheckInterval;

        if (!_war.TryGetWar(planet, out var war, out _))
            return;

        var settings = Settings;
        var fighters = CountFighters();
        var points = _subsectors.Holders(_maps);

        // the sectors of the front each side is holding to take
        var taking = new Dictionary<int, ST14GalaxyFaction>();
        foreach (var cell in FrontCells())
        {
            if (!points.TryGetValue(cell, out var holder) || holder is not { } side || war.Cells[cell].Faction == side)
                continue;

            var enemy = ST14PlanetWar.Enemy(side);
            if (fighters.GetValueOrDefault((cell, enemy)) < settings.DrivenOutBelow)
                taking[cell] = side;
        }

        // a hold that broke starts over
        foreach (var cell in _holds.Keys.ToList())
        {
            if (taking.TryGetValue(cell, out var side) && side == _holds[cell].Side)
                continue;

            _holds.Remove(cell);
            Announce(Loc.GetString("st14-sector-capture-stop", ("code", war.Cells[cell].Code)));
        }

        foreach (var (cell, side) in taking)
        {
            if (_holds.ContainsKey(cell))
                continue;

            _holds[cell] = (side, TimeSpan.Zero);
            Announce(Loc.GetString("st14-sector-capture-start", ("code", war.Cells[cell].Code), ("side", SideName(side)),
                ("minutes", (int) Math.Round(settings.HoldTime.TotalMinutes))));
        }

        // both sides took a sector of the other: neither clock runs while it lasts
        var paused = _holds.Select(h => h.Value.Side).Distinct().Count() > 1;
        if (paused && !_paused)
            Announce(Loc.GetString("st14-sector-capture-paused"));

        _paused = paused;
        if (paused || _holds.Count == 0)
        {
            _war.SetCaptureClock(planet, null);
            return;
        }

        foreach (var cell in _holds.Keys.ToList())
        {
            var (side, held) = _holds[cell];
            held += elapsed;
            _holds[cell] = (side, held);

            if (held < settings.HoldTime)
                continue;

            EndBattle(planet, war, new Dictionary<int, ST14GalaxyFaction> { [cell] = side },
                Loc.GetString("st14-sector-captured", ("code", war.Cells[cell].Code), ("side", SideName(side))));
            return;
        }

        var longest = _holds.Values.Max(h => h.Held);
        _war.SetCaptureClock(planet, now + (settings.HoldTime - longest));
    }

    private void EndBattle(string planet, ST14PlanetWar war, Dictionary<int, ST14GalaxyFaction> captures, string message)
    {
        _decided = true;
        _war.ApplyCaptures(planet, captures);
        _ticker.EndRound(message);
    }

    /// <summary>
    /// Live fighters of each side in each loaded sector, on all its levels: troopers are people with a player behind
    /// them, the hive is every live mob of the hive's faction, players or not
    /// </summary>
    private Dictionary<(int Cell, ST14GalaxyFaction Side), int> CountFighters()
    {
        var counts = new Dictionary<(int, ST14GalaxyFaction), int>();
        var faction = Settings.HiveFaction;

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } map || _mobState.IsDead(uid))
                continue;

            var cell = CellOf(map);
            if (cell < 0)
                continue;

            ST14GalaxyFaction side;
            if (TryComp<NpcFactionMemberComponent>(uid, out var member) && _faction.IsMember((uid, member), faction))
                side = ST14GalaxyFaction.Arachnid;
            else if (HasComp<HumanoidProfileComponent>(uid) && TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind)
                side = ST14GalaxyFaction.Federation;
            else
                continue;

            counts[(cell, side)] = counts.GetValueOrDefault((cell, side)) + 1;
        }

        return counts;
    }

    private string SideName(ST14GalaxyFaction side)
    {
        return Loc.GetString(side == ST14GalaxyFaction.Arachnid ? "st14-sector-side-arachnid" : "st14-sector-side-federation");
    }

    private void Announce(string message)
    {
        _chat.DispatchGlobalAnnouncement(message, Loc.GetString("st14-sector-announcer"), playSound: false, colorOverride: Color.Gold);
    }
}
