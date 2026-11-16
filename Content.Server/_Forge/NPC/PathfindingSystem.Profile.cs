// Forge-Change: optional pathfinding diagnostics kept in the original system via a partial class.
namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    private bool _profileEnabled;
    private TimeSpan _profileNextLog;
    private int _profileTicks;
    private int _profileCompleted;
    private int _profileRebuiltChunks;
    private int _profilePeakQueue;
    private double _profileGridMs;
    private double _profileSearchMs;
    private double _profileMaxGridMs;
    private double _profileMaxSearchMs;

    private void SetProfileEnabled(bool enabled)
    {
        _profileEnabled = enabled;
        _profileNextLog = TimeSpan.Zero;
        _profileTicks = 0;
        _profileCompleted = 0;
        _profileRebuiltChunks = 0;
        _profilePeakQueue = 0;
        _profileGridMs = 0;
        _profileSearchMs = 0;
        _profileMaxGridMs = 0;
        _profileMaxSearchMs = 0;
    }

    private void RecordProfile(double gridMs, double searchMs, int queued, int completed)
    {
        _profileTicks++;
        _profileCompleted += completed;
        _profilePeakQueue = Math.Max(_profilePeakQueue, queued);
        _profileGridMs += gridMs;
        _profileSearchMs += searchMs;
        _profileMaxGridMs = Math.Max(_profileMaxGridMs, gridMs);
        _profileMaxSearchMs = Math.Max(_profileMaxSearchMs, searchMs);

        if (_profileNextLog == TimeSpan.Zero)
            _profileNextLog = _timing.CurTime + TimeSpan.FromSeconds(10);

        if (_timing.CurTime < _profileNextLog)
            return;

        Log.Info($"NPC pathfinding (10s): grid avg={_profileGridMs / _profileTicks:F2}ms max={_profileMaxGridMs:F2}ms, " +
                 $"search avg={_profileSearchMs / _profileTicks:F2}ms max={_profileMaxSearchMs:F2}ms, " +
                 $"queue now={_pathRequests.Count} peak={_profilePeakQueue}, completed={_profileCompleted}, rebuilt chunks={_profileRebuiltChunks}");

        _profileNextLog = _timing.CurTime + TimeSpan.FromSeconds(10);
        _profileTicks = 0;
        _profileCompleted = 0;
        _profileRebuiltChunks = 0;
        _profilePeakQueue = 0;
        _profileGridMs = 0;
        _profileSearchMs = 0;
        _profileMaxGridMs = 0;
        _profileMaxSearchMs = 0;
    }
}
