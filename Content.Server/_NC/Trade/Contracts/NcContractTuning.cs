using System.Numerics;

namespace Content.Server._NC.Trade;

internal static class NcContractTuning
{
    public const int DefaultObjectiveStageGoal = 1;
    public const int DefaultRepairStageGoal = 3;

    public const float MinRepairDoAfterSeconds = 0.1f;
    public const string DefaultContractPinpointerPrototypeId = "PinpointerUniversal";
    public const string DefaultTrackedDeliveryDropoffBeaconPrototypeId = "TradeContractDropoffBeacon";
    public const string DefaultRepairToolQuality = "Welding";
    public const float DefaultRepairDoAfterSeconds = 2f;
    public const string DefaultRepairStageSoundPath = "/Audio/Effects/sparks4.ogg";

    public const int MaxActiveContractPinpointers = 5;
    public const float GhostRoleStoreDeliveryRange = 2.5f;
    public const float TrackedDeliveryStoreRange = 1.5f;
    public const float TrackedDeliveryDropoffRange = 1.5f;
    public static readonly TimeSpan TrackedDeliveryDropoffCheckInterval = TimeSpan.FromSeconds(0.5);
    public static readonly TimeSpan GhostRoleTimeoutCheckInterval = TimeSpan.FromSeconds(1);

    public const float GuardSpawnRingScaleStep = 0.65f;
    public const float GuardSpawnJitterScale = 0.2f;
    public static readonly Vector2[] HuntGuardSpawnOffsets =
    {
        new(0.9f, 0f),
        new(-0.9f, 0f),
        new(0f, 0.9f),
        new(0f, -0.9f),
        new(0.75f, 0.75f),
        new(-0.75f, 0.75f),
        new(0.75f, -0.75f),
        new(-0.75f, -0.75f)
    };

    public const float RepairStageEffectVariation = 0.125f;
    public const float RepairStageEffectVolume = -1f;
    public const float RepairStageJitterAmplitude = 12f;
    public const float RepairStageJitterFrequency = 7f;
    public static readonly TimeSpan RepairStageEffectDuration = TimeSpan.FromSeconds(1.2);
}
