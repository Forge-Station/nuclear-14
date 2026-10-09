using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Nuclear14.Bosses;

[RegisterComponent]
public sealed partial class MilitarySentrybotComponent : Component
{
    [DataField]
    public float FocusTime = 5f;

    [DataField]
    public float AttackCooldown = 4f;

    [DataField]
    public float RocketSpeed = 10f;

    [DataField]
    public float VisionRange = 60f;

    [DataField]
    public float FocusRange = 45f;

    [DataField]
    public EntProtoId Rocket = "N14MilitarySentrybotRocket";

    [DataField]
    public EntProtoId AimIndicator = "N14MilitaryAimIndicator";

    public float FocusAccumulator;

    public EntityUid? Target;

    public EntityUid? AimIndicatorUid;

    public TimeSpan NextAttack;

    public float LosCheckTimer;
}