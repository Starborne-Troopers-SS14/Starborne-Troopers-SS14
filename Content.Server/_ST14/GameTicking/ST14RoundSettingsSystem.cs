using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server._ST14.GameTicking;

/// <summary>
/// The rounds of the war run without the station rules: the default preset has no random station events, meteors or
/// antagonists, and the station event schedulers stay off even if a preset brings one. A server config that sets these
/// cvars still wins
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
    }
}
