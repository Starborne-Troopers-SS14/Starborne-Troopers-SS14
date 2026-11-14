using Content.Shared.Damage;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Weather;

/// <summary>
/// Something falling out of the sky: it marks the ground first, then hits it after <see cref="Delay"/>
/// </summary>
[RegisterComponent]
public sealed partial class ST14WeatherStrikeComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);

    [DataField]
    public float Radius = 1.5f;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public TimeSpan Knockdown = TimeSpan.Zero;

    /// <summary>
    /// Played where it will land when it shows up
    /// </summary>
    [DataField]
    public SoundSpecifier? FallSound;

    [DataField]
    public SoundSpecifier? ImpactSound;

    /// <summary>
    /// Spawned where it lands, the look of the hit
    /// </summary>
    [DataField]
    public EntProtoId? Effect;

    /// <summary>
    /// Left on the ground with this chance: a boulder after a rockfall
    /// </summary>
    [DataField]
    public EntProtoId? Leave;

    [DataField]
    public float LeaveChance;

    /// <summary>
    /// Decals strewn around where it lands, rubble
    /// </summary>
    [DataField]
    public List<string> Decals = new();

    [DataField]
    public int DecalCount;

    /// <summary>
    /// One of these decals is left right where it lands and stays: the burn of a lightning bolt
    /// </summary>
    [DataField]
    public List<string> Crater = new();

    /// <summary>
    /// Width of the crater decal in tiles, it is centred on the hit
    /// </summary>
    [DataField]
    public float CraterSize = 3f;

    /// <summary>
    /// The sky of the whole sector flashes white
    /// </summary>
    [DataField]
    public bool Flash;

    /// <summary>
    /// Explosion type of the hit, none for a plain blow. Plain id: the explosion prototypes are indexed by the server
    /// </summary>
    [DataField]
    public string? Explosion;

    [DataField]
    public float ExplosionIntensity = 20f;

    [DataField]
    public float ExplosionSlope = 5f;

    [DataField]
    public float ExplosionMaxTile = 4f;

    /// <summary>
    /// The screens of the people this close shake
    /// </summary>
    [DataField]
    public float ShakeRange = 8f;

    [ViewVariables]
    public TimeSpan ImpactAt;
}

/// <summary>
/// The weather event going on on a sector map, sent to the clients: they draw it and play its sound
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14SectorWeatherComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<ST14WeatherEventPrototype>? Event;

    [DataField, AutoNetworkedField]
    public TimeSpan Start;

    [DataField, AutoNetworkedField]
    public TimeSpan End;

    /// <summary>
    /// Where the wind blows to, unit length
    /// </summary>
    [DataField, AutoNetworkedField]
    public System.Numerics.Vector2 Wind = new(1, 0);
}

/// <summary>
/// Lightning struck a sector: the screens of the people there flash
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14WeatherFlashEvent : EntityEventArgs
{
    public NetEntity Map;
    public System.Numerics.Vector2 Position;

    public ST14WeatherFlashEvent(NetEntity map, System.Numerics.Vector2 position)
    {
        Map = map;
        Position = position;
    }
}

/// <summary>
/// Under the open sky of a sector with bad weather: moves slower
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ST14WeatherSlowedComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Speed = 1f;
}

public sealed partial class ST14WeatherSlowSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14WeatherSlowedComponent, RefreshMovementSpeedModifiersEvent>(OnRefresh);
        SubscribeLocalEvent<ST14WeatherSlowedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ST14WeatherSlowedComponent, AfterAutoHandleStateEvent>(OnState);
    }

    public void SetSpeed(EntityUid uid, float speed)
    {
        if (MathF.Abs(speed - 1f) < 0.001f)
        {
            RemComp<ST14WeatherSlowedComponent>(uid);
            return;
        }

        var slowed = EnsureComp<ST14WeatherSlowedComponent>(uid);
        if (MathF.Abs(slowed.Speed - speed) < 0.001f)
            return;

        slowed.Speed = speed;
        Dirty(uid, slowed);
        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnRefresh(Entity<ST14WeatherSlowedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Speed, ent.Comp.Speed);
    }

    private void OnShutdown(Entity<ST14WeatherSlowedComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        ent.Comp.Speed = 1f;
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnState(Entity<ST14WeatherSlowedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
    }
}
