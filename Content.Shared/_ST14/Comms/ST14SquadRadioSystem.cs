using Content.Shared.Radio;

namespace Content.Shared._ST14.Comms;

/// <summary>
/// ":h" of a trooper speaks to the squad, like ";" does
/// </summary>
public sealed class ST14SquadRadioSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ST14SquadRadioComponent, GetDefaultRadioChannelEvent>(OnGetDefaultChannel);
    }

    private void OnGetDefaultChannel(EntityUid uid, ST14SquadRadioComponent comp, GetDefaultRadioChannelEvent args)
    {
        args.Channel = ST14SquadRadioComponent.SquadChannel;
    }
}
