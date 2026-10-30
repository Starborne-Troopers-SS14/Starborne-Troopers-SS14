using Content.Shared._ST14.Hive.Components;
using Content.Shared.Chat;
using Content.Shared.Interaction.Events;
using Content.Shared.Pulling.Events;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Shared._ST14.Hive;

/// <summary>
/// Predicted rules of the hive: bugs never hit their own, nobody drags a queen out of her chamber, and bug emotes sound like bugs
/// </summary>
public sealed partial class SharedST14HiveSystem : EntitySystem
{
    /// <summary>
    /// Radio channel of the hive, the only one bugs talk on
    /// </summary>
    public static readonly ProtoId<RadioChannelPrototype> HiveChannel = "ST14Hive";

    [Dependency] private SharedChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ST14HiveMemberComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<ST14HiveMemberComponent, EmoteEvent>(OnEmote);
        SubscribeLocalEvent<ST14HiveMemberComponent, GetDefaultRadioChannelEvent>(OnGetDefaultRadioChannel);
        SubscribeLocalEvent<ST14HiveQueenComponent, BeingPulledAttemptEvent>(OnQueenPullAttempt);
    }

    private void OnAttackAttempt(EntityUid uid, ST14HiveMemberComponent comp, AttackAttemptEvent args)
    {
        if (args.Target is { } target && IsHive(target))
            args.Cancel();
    }

    private void OnEmote(Entity<ST14HiveMemberComponent> ent, ref EmoteEvent args)
    {
        if (args.Handled || ent.Comp.EmoteSounds is not { } sounds)
            return;

        args.Handled = _chat.TryPlayEmoteSound(ent.Owner, ProtoMan.Index(sounds), args.Emote);
    }

    private void OnGetDefaultRadioChannel(EntityUid uid, ST14HiveMemberComponent comp, GetDefaultRadioChannelEvent args)
    {
        args.Channel = HiveChannel;
    }

    private void OnQueenPullAttempt(EntityUid uid, ST14HiveQueenComponent comp, BeingPulledAttemptEvent args)
    {
        args.Cancel();
    }

    /// <summary>
    /// Whether the entity belongs to the hive: any bug or a queen
    /// </summary>
    public bool IsHive(EntityUid uid)
    {
        return HasComp<ST14HiveMemberComponent>(uid) || HasComp<ST14HiveQueenComponent>(uid);
    }
}
