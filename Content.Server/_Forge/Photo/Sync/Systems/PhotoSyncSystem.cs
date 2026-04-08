using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Shared._Forge.Photo.Sync;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Photo.Sync.Systems;

public sealed class PhotoSyncSystem : EntitySystem
{
    private static readonly ResPath ExportPath = new("/Exports/PhotoCameraFrames");
    private const int MaxSizeBytes = 8 * 1024 * 1024;

    private const float PendingTimeoutSeconds = 45f;

    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IResourceManager _resource = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<int, PendingRequest> _pendingRequests = new();
    private int _nextRequestId;

    private sealed record PendingRequest(
        NetUserId UserId,
        string UserName,
        string Ckey,
        string RequestedBy,
        bool IncludeUi,
        ResPath OutputDirectory,
        TimeSpan CreatedAt);

    public readonly record struct PhotoRequestResult(int RequestId, ResPath OutputDirectory);
    public readonly record struct PhotoBatchResult(int RequestedCount, ResPath OutputDirectory);

    public override void Initialize()
    {
        base.Initialize();

        _resource.UserData.CreateDir(ExportPath);
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

            Log.Warning($"Cache request #{id} for {pending.UserName} timed out after {PendingTimeoutSeconds}s (requested by {pending.RequestedBy}). No response received — possible evasion.");
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        var toRemove = _pendingRequests
            .Where(kv => kv.Value.UserId == args.Session.UserId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            _pendingRequests.Remove(id);
            Log.Info($"Cleaned up pending cache request #{id} — player {args.Session.Name} disconnected.");
        }
    }

    public PhotoRequestResult RequestPhoto(ICommonSession target, string requestedBy, bool includeUi)
    {
        var outputDir = CreateBatchDirectory();
        var requestId = RequestPhotoInternal(target, requestedBy, includeUi, outputDir);
        return new PhotoRequestResult(requestId, outputDir);
    }

    public PhotoBatchResult RequestPhotoAll(string requestedBy, bool includeUi)
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
            RequestPhotoInternal(target, requestedBy, includeUi, outputDir);
            count++;
        }

        return new PhotoBatchResult(count, outputDir);
    }

    private int RequestPhotoInternal(ICommonSession target, string requestedBy, bool includeUi, ResPath outputDirectory)
    {
        var requestId = ++_nextRequestId;
        var ckey = ToCkey(target.Name);
        _pendingRequests[requestId] = new PendingRequest(target.UserId, target.Name, ckey, requestedBy, includeUi, outputDirectory, _timing.CurTime);

        RaiseNetworkEvent(new TextureCacheRefreshEvent(requestId, includeUi), target.Channel);
        Log.Info(
            $"Cache refresh #{requestId} requested from {target.Name} (by: {requestedBy}, overlay: {includeUi}, dir: {outputDirectory}).");
        return requestId;
    }

    private void OnFrameResponse(TextureCacheResultEvent ev, EntitySessionEventArgs args)
    {
        if (!_pendingRequests.Remove(ev.Sequence, out var pending))
        {
            Log.Warning($"Received unexpected cache result #{ev.Sequence} from {args.SenderSession.Name}.");
            return;
        }

        if (args.SenderSession.UserId != pending.UserId)
        {
            Log.Warning(
                $"Ignoring cache result #{ev.Sequence}: session mismatch (expected {pending.UserName}, got {args.SenderSession.Name}).");
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
                $"Cache result #{ev.Sequence} for {pending.UserName} has invalid size: {ev.Payload.Length} bytes.");
            return;
        }

        if (!CheckPngSignature(ev.Payload))
        {
            Log.Warning($"Cache result #{ev.Sequence} for {pending.UserName} is not a valid PNG.");
            return;
        }

        try
        {
            var filePath = GetUniquePath(pending, ev.Sequence);

            using var file = _resource.UserData.Open(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(ev.Payload, 0, ev.Payload.Length);

            Log.Info(
                $"Saved cache result #{ev.Sequence} for {pending.UserName} (requested by {pending.RequestedBy}) to {filePath}");
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save cache result #{ev.Sequence} for {pending.UserName}: {e}");
        }
    }

    private ResPath GetUniquePath(PendingRequest pending, int requestId)
    {
        var mode = pending.IncludeUi ? "ui" : "noui";
        var time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var baseName = $"{time}-{pending.Ckey}-{mode}-req{requestId}";

        for (var i = 0; i < 10; i++)
        {
            var suffix = i == 0 ? string.Empty : $"-{i}";
            var path = pending.OutputDirectory / $"{baseName}{suffix}.png";
            if (!_resource.UserData.Exists(path))
                return path;
        }

        return pending.OutputDirectory / $"{baseName}-{Guid.NewGuid():N}.png";
    }

    private ResPath CreateBatchDirectory()
    {
        var time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var baseName = $"batch-{time}";

        for (var i = 0; i < 50; i++)
        {
            var suffix = i == 0 ? string.Empty : $"-{i}";
            var dir = ExportPath / $"{baseName}{suffix}";
            if (_resource.UserData.IsDir(dir))
                continue;

            _resource.UserData.CreateDir(dir);
            return dir;
        }

        var fallback = ExportPath / $"batch-{time}-{Guid.NewGuid():N}";
        _resource.UserData.CreateDir(fallback);
        return fallback;
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

    private static bool CheckPngSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            return false;

        return data[0] == 0x89 &&
               data[1] == 0x50 &&
               data[2] == 0x4E &&
               data[3] == 0x47 &&
               data[4] == 0x0D &&
               data[5] == 0x0A &&
               data[6] == 0x1A &&
               data[7] == 0x0A;
    }
}
