using Content.Shared.Actions;
using Content.Shared.DoAfter;
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
