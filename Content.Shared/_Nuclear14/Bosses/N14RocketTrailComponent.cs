using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Nuclear14.Bosses;

[RegisterComponent]
public sealed partial class N14RocketTrailComponent : Component
{
    [DataField]
    public EntProtoId PuffProto = "N14SmokePuff";

    [DataField]
    public float Interval = 0.08f;

    [DataField]
    public float Offset = 0.7f;

    public float Accumulator;
}