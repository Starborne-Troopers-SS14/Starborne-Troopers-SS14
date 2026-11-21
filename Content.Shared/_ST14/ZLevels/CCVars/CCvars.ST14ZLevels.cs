/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float>
        ST14BaseFallingDamage = CVarDef.Create("st14.zlevels.base_falling_damage", 0.75f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        ST14BaseFallingOtherDamage = CVarDef.Create("st14.zlevels.base_falling_other_damage", 0.4f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        ST14BaseFallingStunTime = CVarDef.Create("st14.zlevels.base_falling_stun_time", 0.1f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        ST14BaseFallingOtherStunTime = CVarDef.Create("st14.zlevels.base_falling_other_stun_time", 0.06f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<int> ZLevelsPhysicsTickRate =
        CVarDef.Create("st14.zlevels.physics.tick_rate", 60, CVar.ARCHIVE);

    public static readonly CVarDef<bool> ZLevelsPhysicsClientSimulation =
        CVarDef.Create("st14.zlevels.physics.client_simulation", true, CVar.ARCHIVE | CVar.CLIENT);

    /**
     * Physics
     */

    public static readonly CVarDef<float>
        ST14ZLevelsPhysicsGravityForce = CVarDef.Create("st14.zlevels.physics.gravity_force", 9.8f, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<float>
        ST14ZLevelsPhysicsVelocityLimit = CVarDef.Create("st14.zlevels.physics.velocity_limit", 20f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// The minimum speed required to trigger LandEvent events.
    /// </summary>
    public static readonly CVarDef<float>
        ST14ZLevelsPhysicsImpactVelocity = CVarDef.Create("st14.zlevels.physics.impact_velocity", 3f, CVar.SERVER | CVar.REPLICATED);

    /**
     * Rendering
     */

    public static readonly CVarDef<int>
        ST14ZLevelsRenderingMaxZLevelsBelowRendering = CVarDef.Create("st14.zlevels.rendering.max_zLevels_below_rendering", 1, CVar.SERVER | CVar.REPLICATED);

    /**
     * Cross level distance
     */

    /// <summary>
    /// How much a single z-level of height counts as in world units when measuring the distance
    /// between two entities on different levels. Used by sound attenuation and z-level chat.
    /// </summary>
    public static readonly CVarDef<float>
        ST14ZLevelsVerticalDistanceScale = CVarDef.Create("st14.zlevels.vertical_distance_scale", 1f, CVar.SERVER | CVar.REPLICATED);

    /**
     * Audio
     */

    /// <summary>
    /// How many decibels of volume are subtracted from a PVS-positioned sound for every Z-level
    /// it is away from the listener.
    /// </summary>
    public static readonly CVarDef<float>
        ST14ZLevelsAudioPerLevelAttenuation = CVarDef.Create("st14.zlevels.audio.per_level_attenuation_db", 9f, CVar.ARCHIVE | CVar.CLIENT);

    /// <summary>
    /// Occlusion added to a cross-Z-level sound when an opaque tile blocks the floor/ceiling between
    /// the source and the listener, on top of the flat per-level attenuation.
    /// </summary>
    public static readonly CVarDef<float>
        ST14ZLevelsAudioFloorOcclusion = CVarDef.Create("st14.zlevels.audio.floor_occlusion", 3f, CVar.ARCHIVE | CVar.CLIENT);
}
