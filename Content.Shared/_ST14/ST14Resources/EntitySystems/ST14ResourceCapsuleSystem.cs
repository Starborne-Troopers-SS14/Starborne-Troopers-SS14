using Content.Shared._ST14.ST14Resources.Components;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.ST14Resources.EntitySystems;

public sealed partial class ST14ResourceCapsuleSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14ResourceCapsuleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ST14ResourceCapsuleComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(Entity<ST14ResourceCapsuleComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnExamined(Entity<ST14ResourceCapsuleComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.ResourceType is not { } type || IsEmpty(ent))
        {
            args.PushMarkup(Loc.GetString("st14-capsule-examine-empty",
                ("capacity", ent.Comp.Capacity.Int())));
            return;
        }

        args.PushMarkup(Loc.GetString("st14-capsule-examine-contents",
            ("resource", Loc.GetString(_prototype.Index(type).Name)),
            ("amount", ent.Comp.Amount.Int()),
            ("capacity", ent.Comp.Capacity.Int())));
    }

    public bool IsFull(Entity<ST14ResourceCapsuleComponent> ent)
    {
        return ent.Comp.Amount >= ent.Comp.Capacity;
    }

    public bool IsEmpty(Entity<ST14ResourceCapsuleComponent> ent)
    {
        return ent.Comp.Amount <= FixedPoint2.Zero;
    }

    public bool IsCompatible(Entity<ST14ResourceCapsuleComponent> ent, ProtoId<ST14ResourcePrototype> resource)
    {
        return ent.Comp.ResourceType == null || ent.Comp.ResourceType == resource || IsEmpty(ent);
    }

    public bool CanAccept(Entity<ST14ResourceCapsuleComponent> ent, ProtoId<ST14ResourcePrototype> resource)
    {
        return !IsFull(ent) && IsCompatible(ent, resource);
    }

    public FixedPoint2 TryAdd(Entity<ST14ResourceCapsuleComponent> ent, ProtoId<ST14ResourcePrototype> resource, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || !CanAccept(ent, resource))
            return FixedPoint2.Zero;

        var added = FixedPoint2.Min(amount, ent.Comp.Capacity - ent.Comp.Amount);
        ent.Comp.ResourceType = resource;
        ent.Comp.Amount += added;

        Dirty(ent);
        UpdateAppearance(ent);
        return added;
    }

    public FixedPoint2 TryRemove(Entity<ST14ResourceCapsuleComponent> ent, ProtoId<ST14ResourcePrototype> resource, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || ent.Comp.ResourceType != resource)
            return FixedPoint2.Zero;

        var removed = FixedPoint2.Min(amount, ent.Comp.Amount);
        ent.Comp.Amount -= removed;

        if (IsEmpty(ent))
        {
            ent.Comp.Amount = FixedPoint2.Zero;
            ent.Comp.ResourceType = null;
        }

        Dirty(ent);
        UpdateAppearance(ent);
        return removed;
    }

    private void UpdateAppearance(Entity<ST14ResourceCapsuleComponent> ent)
    {
        var fraction = ent.Comp.Capacity > FixedPoint2.Zero
            ? (float) (ent.Comp.Amount / ent.Comp.Capacity)
            : 0f;

        _appearance.SetData(ent, ST14ResourceCapsuleVisuals.FillFraction, fraction);

        // ID ресурса для клиента — по нему выбирается спрайт капсулы. Пустая строка = капсула пустая.
        _appearance.SetData(ent, ST14ResourceCapsuleVisuals.Resource, ent.Comp.ResourceType?.Id ?? string.Empty);

        if (ent.Comp.ResourceType is { } type)
            _appearance.SetData(ent, ST14ResourceCapsuleVisuals.Color, _prototype.Index(type).Color);
    }
}
