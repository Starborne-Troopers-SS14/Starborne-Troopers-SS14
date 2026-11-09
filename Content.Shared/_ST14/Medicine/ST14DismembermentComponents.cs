using System.Linq;
using Content.Shared.Body;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Movement.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._ST14.Medicine;

/// <summary>
/// A body whose arms and legs a heavy hit can tear off: claws of the bugs first of all, bullets hardly ever.
/// A limb only comes off a body already badly hurt
/// </summary>
[RegisterComponent]
public sealed partial class ST14DismemberableComponent : Component
{
    /// <summary>
    /// How much each kind of damage of one hit counts towards tearing a limb off
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, float> DamageWeights = new()
    {
        { "Slash", 1f },
        { "Blunt", 0.7f },
        { "Piercing", 0.15f },
        { "Heat", 0.3f },
    };

    /// <summary>
    /// A hit weaker than this never tears anything off
    /// </summary>
    [DataField]
    public float HitThreshold = 14f;

    /// <summary>
    /// The body has to have taken this much damage in all before a limb can come off
    /// </summary>
    [DataField]
    public float WoundThreshold = 55f;

    /// <summary>
    /// Chance for every point of the hit over the threshold
    /// </summary>
    [DataField]
    public float ChancePerPoint = 0.035f;

    [DataField]
    public float MaxChance = 0.55f;

    /// <summary>
    /// Hits of the hive count this much more: they are made to tear
    /// </summary>
    [DataField]
    public float HiveMultiplier = 1.6f;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextAllowed;

    [DataField]
    public SoundSpecifier TearSound = new SoundCollectionSpecifier("gib", AudioParams.Default.WithVolume(4f).WithVariation(0.1f));

    [DataField]
    public SoundSpecifier CrackSound = new SoundCollectionSpecifier("ST14LimbTear", AudioParams.Default.WithVolume(4f).WithVariation(0.08f));

    [DataField]
    public string Scream = "ST14TearScream";

    [DataField]
    public TimeSpan Knockdown = TimeSpan.FromSeconds(2.5);

    [DataField]
    public TimeSpan Shock = TimeSpan.FromSeconds(8);
}

/// <summary>
/// The legs the body has lost: one makes it limp, both leave it crawling. A field prosthesis limps a little
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ST14MissingLegsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Missing;

    [DataField, AutoNetworkedField]
    public int FieldLegs;

    /// <summary>
    /// The arms and legs the body has lost, flesh not replaced by a prosthesis: 1 left arm, 2 right arm,
    /// 4 left leg, 8 right leg. The clothing on them is not drawn
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MissingLimbs;

    [DataField]
    public float OneLegSpeed = 0.5f;

    [DataField]
    public float NoLegsSpeed = 0.2f;

    [DataField]
    public float FieldLegSpeed = 0.85f;
}

/// <summary>
/// Slows a body for the legs it lost, on both sides so walking stays predicted
/// </summary>
public sealed partial class ST14MissingLegsSystem : EntitySystem
{
    private static readonly string[] Legs = { "LegLeft", "LegRight" };

    /// <summary>
    /// The limbs in the order of the bits of <see cref="ST14MissingLegsComponent.MissingLimbs"/>
    /// </summary>
    private static readonly string[] Limbs = { "ArmLeft", "ArmRight", "LegLeft", "LegRight" };

    [Dependency] private BodySystem _body = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ST14MissingLegsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<ST14MissingLegsComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<ST14MissingLegsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _speed.RefreshMovementSpeedModifiers(ent.Owner);
        var ev = new ST14LimbsChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnRefreshSpeed(Entity<ST14MissingLegsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var speed = ent.Comp.Missing switch
        {
            <= 0 => ent.Comp.FieldLegs > 0 ? ent.Comp.FieldLegSpeed : 1f,
            1 => ent.Comp.OneLegSpeed,
            _ => ent.Comp.NoLegsSpeed,
        };

        args.ModifySpeed(speed, speed);
    }

    /// <summary>
    /// Counts the limbs the body has now, flesh or not, after one came off or a prosthesis went on
    /// </summary>
    public void Refresh(EntityUid body)
    {
        var organs = _body.EnumerateOrgans<OrganComponent>(body).ToList();
        var categories = organs.Select(o => o.Comp1.Category?.Id).ToHashSet();
        var legs = organs.Where(o => o.Comp1.Category is { } c && Legs.Contains(c.Id)).ToList();
        var comp = EnsureComp<ST14MissingLegsComponent>(body);
        comp.Missing = Legs.Length - legs.Count;
        comp.MissingLimbs = 0;
        for (var i = 0; i < Limbs.Length; i++)
        {
            if (!categories.Contains(Limbs[i]))
                comp.MissingLimbs |= 1 << i;
        }

        comp.FieldLegs = legs.Count(l => TryComp<ST14ProsthesisComponent>(l, out var prosthesis) && prosthesis.Field);
        Dirty(body, comp);
        _speed.RefreshMovementSpeedModifiers(body);
    }
}

/// <summary>
/// The limbs of a body came in from the server: the clothing on them can be redrawn
/// </summary>
[ByRefEvent]
public record struct ST14LimbsChangedEvent;
