using Robust.Shared.Serialization;

namespace Content.Shared.Body.Part;

/// <summary>
/// Which side of the body a part belongs to.
///
/// Fork-owned, see <see cref="BodyPartType"/>.
/// </summary>
[Serializable, NetSerializable]
public enum BodyPartSymmetry
{
    None = 0,
    Left,
    Right,
}
