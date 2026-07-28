using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Warfront.CapturePoint;

[Serializable, NetSerializable]
public sealed class CapturePointBoundUserInterfaceState : BoundUserInterfaceState
{
    public WarfrontFaction? Owner;
    public bool CaptureInProgress;
    public WarfrontFaction? Attacker;
    public TimeSpan CaptureEndTime;
    public TimeSpan CooldownEndTime;
    public int CaptureDurationSeconds;
    public int CooldownSeconds;
    public int PointsPerMinute;
    public TimeSpan NextPayoutTime;
    public TimeSpan ShopNextRotationTime;
    public LocId Title;
}
