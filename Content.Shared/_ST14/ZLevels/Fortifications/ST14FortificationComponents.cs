/*
 * Starborne Troopers trenches and bunkers on the z-level layers.
 * Part of the ST14 z-levels (see LICENSE.TXT in the ZLevels folder).
 */

using Content.Shared.DoAfter;
using Content.Shared.Maps;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.ZLevels.Fortifications;

/// <summary>
/// A tool that digs a trench: used on the ground of a level, it opens the tile into the level below and turns the
/// tile under it into the floor of a trench walled with earth.
/// The entrenching tool also digs small cable trenches (<c>ST14TrenchTool</c>): using it in hand switches between the two.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ST14TrenchDiggerComponent : Component
{
    [DataField]
    public TimeSpan DigTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Digs cable trenches instead of trenches into the level below
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CableMode;
}

/// <summary>
/// A frame that lays a roof of tiles on the level above itself and then disappears: built inside a bunker or over a
/// dug-out, it covers it from the sky, the light and the weather.
/// </summary>
[RegisterComponent]
public sealed partial class ST14RoofPlacerComponent : Component
{
    [DataField]
    public ProtoId<ContentTileDefinition> Tile = "FloorReinforced";

    /// <summary>
    /// 0 covers the tile of the frame, 1 a 3x3 square around it, and so on
    /// </summary>
    [DataField]
    public int Radius = 1;
}

[Serializable, NetSerializable]
public sealed partial class ST14DigTrenchDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetCoordinates Location;

    private ST14DigTrenchDoAfterEvent()
    {
    }

    public ST14DigTrenchDoAfterEvent(NetCoordinates location)
    {
        Location = location;
    }

    public override DoAfterEvent Clone()
    {
        return this;
    }
}
