using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Foldable;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared._ST14.Clothing;

/// <summary>
/// "Roll up / roll down the sleeves" verb on uniforms that have a sprite for it.
/// </summary>
public sealed partial class ST14RollableSleevesSystem : EntitySystem
{
    [Dependency] private ClothingSystem _clothing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14RollableSleevesComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ST14RollableSleevesComponent, MapInitEvent>(OnMapInit);
        // putting the jacket back on clears the prefix, the rolled sleeves come back after that
        SubscribeLocalEvent<ST14RollableSleevesComponent, FoldedEvent>(OnFolded,
            after: [typeof(FoldableClothingSystem)]);
    }

    private void OnMapInit(Entity<ST14RollableSleevesComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Rolled && !IsFolded(ent))
            _clothing.SetEquippedPrefix(ent, ent.Comp.RolledEquippedPrefix);
    }

    private void OnGetVerbs(Entity<ST14RollableSleevesComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        // without the jacket there are no sleeves to roll
        if (IsFolded(ent))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.Rolled ? "st14-verb-sleeves-roll-down" : "st14-verb-sleeves-roll-up"),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/fold.svg.192dpi.png")),
            Act = () => SetRolled(ent, !ent.Comp.Rolled),
        });
    }

    public void SetRolled(Entity<ST14RollableSleevesComponent> ent, bool rolled)
    {
        if (ent.Comp.Rolled == rolled)
            return;

        ent.Comp.Rolled = rolled;
        Dirty(ent);

        if (!IsFolded(ent))
            _clothing.SetEquippedPrefix(ent, rolled ? ent.Comp.RolledEquippedPrefix : null);
    }

    private void OnFolded(Entity<ST14RollableSleevesComponent> ent, ref FoldedEvent args)
    {
        if (!args.IsFolded && ent.Comp.Rolled)
            _clothing.SetEquippedPrefix(ent, ent.Comp.RolledEquippedPrefix);
    }

    private bool IsFolded(EntityUid uid)
    {
        return TryComp<FoldableComponent>(uid, out var foldable) && foldable.IsFolded;
    }
}
