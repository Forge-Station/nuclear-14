using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Shared._Forge.Photo;
using Content.Shared._Forge.Rendering.Cache;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Rendering.Cache;

public sealed class TextureCacheValidationSystem : EntitySystem
{
    private const string UserDataDirectoryName = "Space Station 14";
    private static readonly string ExportPath = Path.Combine(GetExportsRootPath(), "TextureCacheFrames");
    private const int MaxSizeBytes = 8 * 1024 * 1024;
    private const float PendingTimeoutSeconds = 45f;

    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<int, PendingRequest> _pendingRequests = new();
    private int _nextRequestId;

    private sealed record PendingRequest(
        NetUserId UserId,
        string UserName,
        string Ckey,
        string RequestedBy,
        bool IncludeUi,
        string OutputDirectory,
        TimeSpan CreatedAt);

    public readonly record struct RequestResult(int RequestId, string OutputDirectory);
    public readonly record struct BatchResult(int RequestedCount, string OutputDirectory);

    public override void Initialize()
    {
        base.Initialize();

        Directory.CreateDirectory(ExportPath);
        SubscribeNetworkEvent<TextureCacheResultEvent>(OnFrameResponse);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingRequests.Count == 0)
            return;

        var now = _timing.CurTime;
        List<int>? expired = null;

        foreach (var (id, pending) in _pendingRequests)
        {
            if ((now - pending.CreatedAt).TotalSeconds < PendingTimeoutSeconds)
                continue;

            expired ??= new List<int>();
            expired.Add(id);
        }

        if (expired == null)
            return;

        foreach (var id in expired)
        {
            if (!_pendingRequests.Remove(id, out var pending))
                continue;

            Log.Warning($"Cache request #{id} for {pending.UserName} timed out after {PendingTimeoutSeconds}s (requested by {pending.RequestedBy}).");
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        List<int>? toRemove = null;
        foreach (var (id, pending) in _pendingRequests)
        {
            if (pending.UserId != args.Session.UserId)
                continue;

            toRemove ??= new List<int>();
            toRemove.Add(id);
        }

        if (toRemove == null)
            return;

        foreach (var id in toRemove)
        {
            _pendingRequests.Remove(id);
            Log.Info($"Cleaned up pending cache request #{id} — player {args.Session.Name} disconnected.");
        }
    }

    public RequestResult RequestCapture(ICommonSession target, string requestedBy, bool includeUi)
    {
        var outputDir = CreateBatchDirectory();
        var requestId = RequestInternal(target, requestedBy, includeUi, outputDir);
        return new RequestResult(requestId, outputDir);
    }

    public BatchResult RequestCaptureAll(string requestedBy, bool includeUi)
    {
        var outputDir = CreateBatchDirectory();

        var sessions = _players
            .Sessions
            .Where(session => session.Status != SessionStatus.Disconnected)
            .Cast<ICommonSession>()
            .ToArray();

        var count = 0;
        foreach (var target in sessions)
        {
            RequestInternal(target, requestedBy, includeUi, outputDir);
            count++;
        }

        return new BatchResult(count, outputDir);
    }

    private int RequestInternal(ICommonSession target, string requestedBy, bool includeUi, string outputDirectory)
    {
        var requestId = ++_nextRequestId;
        var ckey = ToCkey(target.Name);
        _pendingRequests[requestId] = new PendingRequest(target.UserId, target.Name, ckey, requestedBy, includeUi, outputDirectory, _timing.CurTime);

        RaiseNetworkEvent(new TextureCacheRefreshEvent(requestId, includeUi), target.Channel);
        Log.Info(
            $"Cache refresh #{requestId} from {target.Name} (by: {requestedBy}, overlay: {includeUi}).");
        return requestId;
    }

    private void OnFrameResponse(TextureCacheResultEvent ev, EntitySessionEventArgs args)
    {
        if (!_pendingRequests.Remove(ev.Sequence, out var pending))
        {
            Log.Warning($"Unexpected cache result #{ev.Sequence} from {args.SenderSession.Name}.");
            return;
        }

        if (args.SenderSession.UserId != pending.UserId)
        {
            Log.Warning(
                $"Cache result #{ev.Sequence}: session mismatch (expected {pending.UserName}, got {args.SenderSession.Name}).");
            return;
        }

        if (!ev.Success)
        {
            Log.Warning(
                $"Cache result #{ev.Sequence} failed for {pending.UserName}. Detail: {ev.Detail}");
            return;
        }

        if (ev.Payload.Length == 0 || ev.Payload.Length > MaxSizeBytes)
        {
            Log.Warning(
                $"Cache result #{ev.Sequence} for {pending.UserName}: invalid payload size ({ev.Payload.Length} bytes).");
            return;
        }

        if (!PngUtility.CheckSignature(ev.Payload))
        {
            Log.Warning($"Cache result #{ev.Sequence} for {pending.UserName}: invalid format.");
            return;
        }

        try
        {
            var filePath = GetUniquePath(pending, ev.Sequence);

            using var file = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(ev.Payload, 0, ev.Payload.Length);

            Log.Info(
                $"Saved cache #{ev.Sequence} for {pending.UserName} (by {pending.RequestedBy}) → {filePath}");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save cache #{ev.Sequence} for {pending.UserName}: {e}");
        }
    }

    private string GetUniquePath(PendingRequest pending, int requestId)
    {
        var mode = pending.IncludeUi ? "full" : "base";
        var time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var baseName = $"{time}-{pending.Ckey}-{mode}-{requestId}";

        for (var i = 0; i < 10; i++)
        {
            var suffix = i == 0 ? string.Empty : $"-{i}";
            var path = Path.Combine(pending.OutputDirectory, $"{baseName}{suffix}.png");
            if (!File.Exists(path))
                return path;
        }

        return Path.Combine(pending.OutputDirectory, $"{baseName}-{Guid.NewGuid():N}.png");
    }

    private string CreateBatchDirectory()
    {
        var time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var baseName = $"batch-{time}";

        for (var i = 0; i < 50; i++)
        {
            var suffix = i == 0 ? string.Empty : $"-{i}";
            var dir = Path.Combine(ExportPath, $"{baseName}{suffix}");
            if (Directory.Exists(dir))
                continue;

            Directory.CreateDirectory(dir);
            return dir;
        }

        var fallback = Path.Combine(ExportPath, $"batch-{time}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static string GetExportsRootPath()
    {
        string appDataDir;

#if LINUX
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        appDataDir = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdgDataHome;
#elif MACOS
        appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support");
#else
        appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#endif

        return Path.Combine(appDataDir, UserDataDirectoryName, "data", "Exports");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[value.Length];
        var count = 0;

        foreach (var ch in value)
        {
            buffer[count++] = Array.IndexOf(invalid, ch) != -1 ? '_' : ch;
        }

        var sanitized = new string(buffer[..count]).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "player" : sanitized;
    }

    private static string ToCkey(string userName)
    {
        var lowered = userName.Trim().ToLowerInvariant();
        return SanitizeFileName(lowered).Replace(' ', '_');
    }

}
