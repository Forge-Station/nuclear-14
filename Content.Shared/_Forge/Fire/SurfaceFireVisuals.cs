using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Fire;

[Serializable, NetSerializable]
public enum SurfaceFireVisuals : byte
{
    Stage,
}

[Serializable, NetSerializable]
public enum SurfaceFireStage : byte
{
    Small,
    Medium,
    Large,
    Initial,
}
