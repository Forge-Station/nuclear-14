using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Warps;

[Serializable, NetSerializable]
public sealed partial class WarpDoAfterEvent : SimpleDoAfterEvent;
