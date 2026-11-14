using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._ST14.GameTicking;

/// <summary>
/// The rounds of the war run without the station rules: the default preset has no random station events, meteors or
/// antagonists, and the station event schedulers stay off even if a preset brings one. There is no Central Command
/// either: no emergency shuttle, no arrivals terminal, no late join arrival announcements. A server config that sets
/// these cvars still wins
/// </summary>
public sealed partial class ST14RoundSettingsSystem : EntitySystem
{
    private const string WarPreset = "ST14War";

    [Dependency] private IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OverrideDefault(CCVars.GameLobbyDefaultPreset, WarPreset);
        _cfg.OverrideDefault(CCVars.GameLobbyFallbackPreset, WarPreset);
        _cfg.OverrideDefault(CCVars.EventsEnabled, false);

        // No Central Command: no emergency shuttle to call or announce, no arrivals terminal, no auto-called evacuation
        _cfg.OverrideDefault(CCVars.EmergencyShuttleEnabled, false);
        _cfg.OverrideDefault(CCVars.EmergencyShuttleAutoCallTime, 0);
        _cfg.OverrideDefault(CCVars.EmergencyShuttleAutoCallExtensionTime, 0);
        _cfg.OverrideDefault(CCVars.ArrivalsShuttles, false);

        // a new player starts as a trooper, not as the passenger of the stock game
        _cfg.OverrideDefault(CCVars.NewCharacterJobs, "ST14Trooper");

        // the round ends with the credits, not with a jingle
        _cfg.OverrideDefault(CCVars.RoundEndSoundCollection, string.Empty);
    }
}
