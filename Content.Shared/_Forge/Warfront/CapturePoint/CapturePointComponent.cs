namespace Content.Shared._Forge.Warfront.CapturePoint;

[RegisterComponent]
public sealed partial class CapturePointComponent : Component
{
    [DataField]
    public LocId Title = "capture-point-window-title-outpost";

    [DataField]
    public TimeSpan CaptureDuration = TimeSpan.FromMinutes(2);

    [DataField]
    public TimeSpan CaptureCooldown = TimeSpan.FromSeconds(30);

    [DataField]
    public int PointsPerMinute = 1;

    [DataField]
    public TimeSpan? VictoryHoldDuration;

    [DataField]
    public WarfrontFaction? OwnerFaction;

    [DataField]
    public bool CaptureInProgress;

    [DataField]
    public WarfrontFaction? Attacker;

    [DataField]
    public TimeSpan CaptureEndTime;

    [DataField]
    public TimeSpan CooldownEndTime;

    [DataField]
    public TimeSpan NextPayoutTime;

    [DataField]
    public TimeSpan VictoryTime;
}
