using System.Numerics;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._ST14.Hive;

public sealed partial class ST14HiveEvolveActionEvent : InstantActionEvent;

public sealed partial class ST14EatCorpseActionEvent : EntityTargetActionEvent;

public sealed partial class ST14BrainSuckActionEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class ST14EatCorpseDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ST14BrainSuckDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public enum ST14BrainBugVisuals : byte
{
    Sucking,
}

public sealed partial class ST14BugLungeActionEvent : WorldTargetActionEvent;

public sealed partial class ST14BugRendActionEvent : InstantActionEvent;

public sealed partial class ST14BugGrabActionEvent : EntityTargetActionEvent;

public sealed partial class ST14BugBarricadeActionEvent : InstantActionEvent;

public sealed partial class ST14BugExecuteActionEvent : EntityTargetActionEvent;

/// <summary>
/// Blood trails of the wounded troopers, sent only to the bugs that can smell them
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BloodScentEvent(List<ST14BloodTrail> trails) : EntityEventArgs
{
    public readonly List<ST14BloodTrail> Trails = trails;
}

/// <summary>
/// Trail of one trooper: points on the map from the oldest to the newest and how many seconds ago each was left
/// </summary>
[Serializable, NetSerializable]
public sealed class ST14BloodTrail
{
    public NetEntity Trooper;
    public MapId Map;
    public List<Vector2> Points = new();
    public List<float> Ages = new();
}
