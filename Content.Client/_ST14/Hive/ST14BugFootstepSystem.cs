using Content.Shared._ST14.CCVar;
using Content.Shared._ST14.Hive.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._ST14.Hive;

/// <summary>
/// Bug footsteps, played by every client for the bugs it sees: the sound depends on the surface under the bug and on
/// whether it walks or runs, and its volume on the player's own slider. Bugs do not have the FootstepSound tag,
/// so the regular footsteps stay silent for them
/// </summary>
public sealed partial class ST14BugFootstepSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>
    /// Which bug surface each tile footstep collection sounds like
    /// </summary>
    private static readonly Dictionary<string, string> Surfaces = new()
    {
        ["FootstepTile"] = "concrete",
        ["FootstepFloor"] = "concrete",
        ["FootstepCarpet"] = "concrete",
        ["FootstepWood"] = "concrete",
        ["FootstepHull"] = "metal",
        ["FootstepPlating"] = "metal",
        ["FootstepCatwalk"] = "metal_thin",
        ["FootstepBlood"] = "gore",
        ["FootstepAsteroid"] = "sand",
        ["FootstepGrass"] = "sand",
        ["FootstepSnow"] = "sand",
    };

    private float _volume = 1f;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, ST14CVars.BugFootstepsVolume, value => _volume = value, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<ST14BugFootstepsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var steps, out var xform))
        {
            var coords = xform.Coordinates;
            var last = steps.LastPosition;
            steps.LastPosition = coords;

            if (last == null || !coords.TryDistance(EntityManager, last.Value, out var moved) || moved > 2f)
            {
                steps.Distance = 0;
                continue;
            }

            if (moved <= 0.001f || frameTime <= 0f || !_mobState.IsAlive(uid))
                continue;

            // Other players' sprint key is not known here, so running is told apart by speed
            var walkSpeed = CompOrNull<MovementSpeedModifierComponent>(uid)?.CurrentWalkSpeed ?? 2.5f;
            var running = moved / frameTime > walkSpeed * 1.15f;

            steps.Distance += moved;
            var needed = running ? steps.RunDistance : steps.WalkDistance;
            if (steps.Distance < needed)
                continue;

            steps.Distance -= needed;

            if (_volume <= 0f)
                continue;

            var sounds = running ? steps.Run : steps.Walk;
            if (!sounds.TryGetValue(GetSurface(steps, xform), out var sound) &&
                !sounds.TryGetValue(steps.DefaultSurface, out sound))
                continue;

            var audioParams = sound.Params
                .WithVariation(sound.Params.Variation ?? 0.08f)
                .AddVolume(SharedAudioSystem.GainToVolume(_volume));

            _audio.PlayPvs(sound, uid, audioParams);
        }
    }

    private string GetSurface(ST14BugFootstepsComponent steps, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_map.TryGetTileRef(gridUid, grid, xform.Coordinates, out var tileRef) ||
            _tileDefs[tileRef.Tile.TypeId] is not ContentTileDefinition tileDef ||
            tileDef.FootstepSounds is not SoundCollectionSpecifier { Collection: { } collection })
        {
            return steps.DefaultSurface;
        }

        return Surfaces.GetValueOrDefault(collection.Id, steps.DefaultSurface);
    }
}
