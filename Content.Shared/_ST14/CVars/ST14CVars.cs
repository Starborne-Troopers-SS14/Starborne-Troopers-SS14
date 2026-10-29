using Robust.Shared.Configuration;

namespace Content.Shared._ST14.CCVar;

[CVarDefs]
public static partial class ST14CVars
{
    //game-wide st14 difficulty cvar
    public static readonly CVarDef<float> DifficultyMultiplier =
        CVarDef.Create("st14.difficulty_multiplier", 1f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<string> ClientLanguage =
        CVarDef.Create("st14.language", "", CVar.ARCHIVE);

    public static readonly CVarDef<string> ServerLanguage =
        CVarDef.Create("st14.server_language", "", CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Main menu backgrounds: supersampled rendering and a smoothed animation clock. Off draws them straight to the screen
    /// </summary>
    public static readonly CVarDef<bool> HubSmooth =
        CVarDef.Create("st14.hub_smooth", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Supersampling factor of the main menu backgrounds, 1 renders at screen resolution
    /// </summary>
    public static readonly CVarDef<float> HubSupersample =
        CVarDef.Create("st14.hub_supersample", 2f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Logs frame pacing and on-screen motion of the main menu backgrounds every few seconds
    /// </summary>
    public static readonly CVarDef<bool> HubDebug =
        CVarDef.Create("st14.hub_debug", false, CVar.CLIENTONLY);

    /// <summary>
    /// Forces a lobby theme by id, for example ST14Arachnids, empty picks it from the selected character
    /// </summary>
    public static readonly CVarDef<string> LobbyTheme =
        CVarDef.Create("st14.lobby_theme", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Supporter perks picked on the support screen, sent to the server on connect. Empty means not picked
    /// </summary>
    public static readonly CVarDef<string> PerkOocColor =
        CVarDef.Create("st14.perk_ooc_color", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> PerkGhostColor =
        CVarDef.Create("st14.perk_ghost_color", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> PerkWeapon =
        CVarDef.Create("st14.perk_weapon", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> PerkNickname =
        CVarDef.Create("st14.perk_nickname", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Show the main menu hub instead of the lobby after connecting. Integration tests turn it off to get the stock lobby flow
    /// </summary>
    public static readonly CVarDef<bool> HubOnConnect =
        CVarDef.Create("st14.hub_on_connect", true, CVar.CLIENTONLY);
}
