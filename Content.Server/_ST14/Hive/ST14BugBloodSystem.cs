using System.Numerics;
using Content.Server.Decals;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Decals;
using Content.Shared.Mobs;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._ST14.Hive;

/// <summary>
/// Green bug blood: a splash decal lands next to a bug every time it gets hurt, and a few more when it dies
/// </summary>
public sealed partial class ST14BugBloodSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    // Splash sprites are 48 px, a decal is placed by its corner
    private static readonly Vector2 DecalCenter = new(0.75f, 0.75f);

    private readonly Dictionary<string, List<string>> _decalsByTag = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14BugBloodComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<ST14BugBloodComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<DecalPrototype>())
            _decalsByTag.Clear();
    }

    private void OnDamageChanged(Entity<ST14BugBloodComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || _timing.CurTime < ent.Comp.NextSplash)
            return;

        ent.Comp.NextSplash = _timing.CurTime + ent.Comp.Cooldown;
        Splash(ent);
    }

    private void OnMobStateChanged(Entity<ST14BugBloodComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        for (var i = 0; i < ent.Comp.DeathSplashes; i++)
        {
            Splash(ent);
        }
    }

    /// <summary>
    /// Puts one random splash of blood on the ground near the bug
    /// </summary>
    public void Splash(Entity<ST14BugBloodComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid)
            return;

        var decals = GetDecals(ent.Comp.DecalTag);
        if (decals.Count == 0)
            return;

        var offset = _random.NextVector2(ent.Comp.Spread);
        var local = _transform.WithEntityId(xform.Coordinates, grid).Position + offset - DecalCenter;

        _decals.TryAddDecal(_random.Pick(decals),
            new EntityCoordinates(grid, local),
            out _,
            rotation: _random.NextAngle(),
            cleanable: true);
    }

    private List<string> GetDecals(string tag)
    {
        if (_decalsByTag.TryGetValue(tag, out var list))
            return list;

        list = new List<string>();
        foreach (var decal in _prototype.EnumeratePrototypes<DecalPrototype>())
        {
            if (decal.Tags.Contains(tag))
                list.Add(decal.ID);
        }

        _decalsByTag[tag] = list;
        return list;
    }
}
