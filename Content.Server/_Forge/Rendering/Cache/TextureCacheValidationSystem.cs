using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Shared._Forge.Rendering.Cache;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Rendering.Cache;

public sealed class TextureCacheValidationSystem : EntitySystem
{
    private const string ClientExportPath = "/Exports/TextureCacheFrames";
    private const int MaxSizeBytes = 8 * 1024 * 1024;
    private const float PendingTimeoutSeconds = 45f;

    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<int, PendingRequest> _pendingRequests = new();
    private int _nextBatchSerial;
    private int _nextRequestId;

    private sealed record PendingRequest(
        NetUserId UserId,
        string UserName,
        string Ckey,
        NetUserId RequestedByUserId,
        string RequestedBy,
        bool IncludeUi,
        string BatchName,
        TimeSpan CreatedAt);

    public readonly record struct RequestResult(int RequestId, string OutputDirectory);
    public readonly record struct BatchResult(int RequestedCount, string OutputDirectory);

    public override void Initialize()
    {
        base.Initialize();

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

    public RequestResult RequestCapture(ICommonSession target, ICommonSession requestedBySession, string requestedBy, bool includeUi)
    {
        var (outputDirectory, batchName) = CreateOutputTarget();
        var requestId = RequestInternal(target, requestedBySession, requestedBy, includeUi, batchName);
        return new RequestResult(requestId, outputDirectory);
    }

    public BatchResult RequestCaptureAll(ICommonSession requestedBySession, string requestedBy, bool includeUi)
    {
        var (outputDirectory, batchName) = CreateOutputTarget();

        var sessions = _players
            .Sessions
            .Where(session => session.Status != SessionStatus.Disconnected)
            .Cast<ICommonSession>()
            .ToArray();

        var count = 0;
        foreach (var target in sessions)
        {
            RequestInternal(target, requestedBySession, requestedBy, includeUi, batchName);
            count++;
        }

        return new BatchResult(count, outputDirectory);
    }

    private int RequestInternal(
        ICommonSession target,
        ICommonSession requestedBySession,
        string requestedBy,
        bool includeUi,
        string batchName)
    {
        var requestId = ++_nextRequestId;
        var ckey = ToCkey(target.Name);
        _pendingRequests[requestId] = new PendingRequest(
            target.UserId,
            target.Name,
            ckey,
            requestedBySession.UserId,
            requestedBy,
            includeUi,
            batchName,
            _timing.CurTime);

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
            TryForwardToRequester(pending, ev.Sequence, false, Array.Empty<byte>(), ev.Detail);
            return;
        }

        if (ev.Payload.Length == 0 || ev.Payload.Length > MaxSizeBytes)
        {
            const string detail = "invalid payload size";
            Log.Warning(
                $"Cache result #{ev.Sequence} for {pending.UserName}: invalid payload size ({ev.Payload.Length} bytes).");
            TryForwardToRequester(pending, ev.Sequence, false, Array.Empty<byte>(), detail);
            return;
        }

        if (!TextureCachePngUtility.CheckSignature(ev.Payload))
        {
            const string detail = "invalid PNG format";
            Log.Warning($"Cache result #{ev.Sequence} for {pending.UserName}: {detail}.");
            TryForwardToRequester(pending, ev.Sequence, false, Array.Empty<byte>(), detail);
            return;
        }

        if (TryForwardToRequester(pending, ev.Sequence, true, ev.Payload, string.Empty))
            return;

        Log.Warning(
            $"Cache result #{ev.Sequence} for {pending.UserName} dropped: requester is offline and server-side saving is disabled.");
    }

    private bool TryForwardToRequester(PendingRequest pending, int sequence, bool success, byte[] payload, string detail)
    {
        if (!_players.TryGetSessionById(pending.RequestedByUserId, out var requester) ||
            requester.Status == SessionStatus.Disconnected)
            return false;

        RaiseNetworkEvent(
            new TextureCacheDeliveryEvent(
                sequence,
                success,
                pending.UserName,
                pending.Ckey,
                pending.IncludeUi,
                pending.BatchName,
                payload,
                detail),
            requester.Channel);

        if (success)
        {
            Log.Info(
                $"Forwarded cache #{sequence} for {pending.UserName} to requester {requester.Name}.");
        }
        else
        {
            Log.Warning(
                $"Forwarded failed cache #{sequence} for {pending.UserName} to requester {requester.Name}: {detail}");
        }

        return true;
    }

    private (string OutputDirectory, string BatchName) CreateOutputTarget()
    {
        var batchName = CreateBatchName();
        return ($"{ClientExportPath}/{batchName}", batchName);
    }

    private string CreateBatchName()
    {
        var serial = ++_nextBatchSerial;
        var time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        return $"batch-{time}-{serial}";
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
