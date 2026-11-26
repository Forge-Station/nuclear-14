// Forge-Change: optional steering diagnostics kept in the original system via a partial class.
namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    private bool _profileEnabled;
    private TimeSpan _profileNextLog;
    private int _profileTicks;
    private int _profileRequests;
    private int _profileActiveSum;
    private int _profileFollowingSum;
    private double _profileSteerMs;
    private double _profileMaxSteerMs;

    private void SetProfileEnabled(bool enabled)
    {
        _profileEnabled = enabled;
        _profileNextLog = TimeSpan.Zero;
        _profileTicks = 0;
        _profileRequests = 0;
        _profileActiveSum = 0;
        _profileFollowingSum = 0;
        _profileSteerMs = 0;
        _profileMaxSteerMs = 0;
    }

    private void RecordSteeringProfile(double steerMs, int active, int following)
    {
        _profileTicks++;
        _profileActiveSum += active;
        _profileFollowingSum += following;
        _profileSteerMs += steerMs;
        _profileMaxSteerMs = Math.Max(_profileMaxSteerMs, steerMs);

        if (_profileNextLog == TimeSpan.Zero)
            _profileNextLog = _timing.CurTime + TimeSpan.FromSeconds(10);

        if (_timing.CurTime < _profileNextLog)
            return;

        Log.Info($"NPC steering (10s): avg={_profileSteerMs / _profileTicks:F2}ms max={_profileMaxSteerMs:F2}ms, " +
                 $"active avg={(double)_profileActiveSum / _profileTicks:F0}, following avg={(double)_profileFollowingSum / _profileTicks:F0}, " +
                 $"new paths={_profileRequests}");

        _profileNextLog = _timing.CurTime + TimeSpan.FromSeconds(10);
        _profileTicks = 0;
        _profileRequests = 0;
        _profileActiveSum = 0;
        _profileFollowingSum = 0;
        _profileSteerMs = 0;
        _profileMaxSteerMs = 0;
    }
}
