using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Sectors;

/// <summary>
/// A place a player of a side can come into the battle at besides the usual landing: a bunker base now, the Rodger
/// Young later. Where it stands in the tiles of its sector, and how many soldier supplies it has left - every trooper
/// who comes in there takes one
/// </summary>
[Serializable, NetSerializable]
public readonly record struct ST14LandingPoint(NetEntity Base, int Cell, string Code, Vector2 Position, int Supplies);

/// <summary>
/// The player is about to join the round and asks where they can come in
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14LandingRequestEvent : EntityEventArgs;

/// <summary>
/// Where the player can come in: the planet and region of the battle and every landing point open to their side. With
/// no points the player joins at the usual landing straight away
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14LandingOptionsEvent(string planet, int region, ST14LandingPoint[] points) : EntityEventArgs
{
    public readonly string Planet = planet;
    public readonly int Region = region;
    public readonly ST14LandingPoint[] Points = points;
}

/// <summary>
/// The player picked a landing point for their next spawn, or the usual landing (null)
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14LandingChooseEvent(NetEntity? point) : EntityEventArgs
{
    public readonly NetEntity? Point = point;
}
