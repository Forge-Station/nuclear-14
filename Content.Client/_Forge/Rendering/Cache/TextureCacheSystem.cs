using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Client.Viewport;
using Content.Shared._Forge.Rendering.Cache;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Shared.Asynchronous;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Rendering.Cache;

/// <summary>
/// Handles texture cache validation requests from the server.
/// </summary>
public sealed class TextureCacheSystem : EntitySystem
{
    private static readonly ResPath ExportRoot = new("/Exports/TextureCacheFrames");
    private const int MaxSizeBytes = 8 * 1024 * 1024;
    private const int MaxPendingSaves = 256;

    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IResourceManager _resManager = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;

    private CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<PendingDelivery> _pendingSaves = new();
    private int _pendingSaveCount;
    private bool _acceptSaveRequests = true;

    private sealed record PendingDelivery(
        int Sequence,
        string SourceUserName,
        string SourceCkey,
        bool IncludeOverlay,
        string BatchName,
        byte[] Payload);

    public override void Initialize()
    {
        base.Initialize();
        _acceptSaveRequests = true;
        _resManager.UserData.CreateDir(ExportRoot);
        SubscribeNetworkEvent<TextureCacheRefreshEvent>(OnRefreshRequest);
        SubscribeNetworkEvent<TextureCacheDeliveryEvent>(OnDeliveryReceived);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cts.Cancel();
        _cts.Dispose();

        _acceptSaveRequests = false;
        DrainPendingSaves(int.MaxValue);
    }

    private void OnRefreshRequest(TextureCacheRefreshEvent ev, EntitySessionEventArgs _)
    {
        try
        {
            if (ev.IncludeOverlay)
            {
                _clyde.Screenshot(ScreenshotType.Final, image => EncodeAndSend(ev.Sequence, image));
                return;
            }

            if (_state.CurrentState is not IMainViewportState state)
            {
                SendResult(new TextureCacheResultEvent(
                    ev.Sequence,
                    false,
                    Array.Empty<byte>(),
                    "Current state does not support viewport capture."));
                return;
            }

            state.Viewport.Viewport.Screenshot(image => EncodeAndSend(ev.Sequence, image));
        }
        catch (Exception e)
        {
            Logger.ErrorS("clyde.tex", $"Cache refresh failed: {e}");
            SendResult(new TextureCacheResultEvent(ev.Sequence, false, Array.Empty<byte>(), e.Message));
        }
    }

    private void EncodeAndSend<T>(int sequence, Image<T> screenshot) where T : unmanaged, IPixel<T>
    {
        var token = _cts.Token;

        _ = Task.Run(() =>
        {
            if (token.IsCancellationRequested)
            {
                screenshot.Dispose();
                return;
            }

            try
            {
                using var frame = screenshot;
                using var data = new MemoryStream();
                frame.SaveAsPng(data);
                var bytes = data.ToArray();

                if (!token.IsCancellationRequested)
                {
                    _taskManager.RunOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested)
                            SendResult(new TextureCacheResultEvent(sequence, true, bytes, string.Empty));
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown.
            }
            catch (Exception e)
            {
                Logger.ErrorS("clyde.tex", $"Cache encode failed: {e}");
                if (!token.IsCancellationRequested)
                {
                    _taskManager.RunOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested)
                            SendResult(new TextureCacheResultEvent(sequence, false, Array.Empty<byte>(), e.Message));
                    });
                }
            }
        }, token);
    }

    private void SendResult(TextureCacheResultEvent ev)
    {
        RaiseNetworkEvent(ev);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (GetPendingSaveCount() == 0)
            return;

        // Keep per-frame work bounded to avoid large hitches under heavy batches.
        var maxPerTick = GetPendingSaveCount() > 64 ? 4 : 1;
        DrainPendingSaves(maxPerTick);
    }

    private void OnDeliveryReceived(TextureCacheDeliveryEvent ev, EntitySessionEventArgs _)
    {
        if (!ev.Success)
        {
            Logger.WarningS("clyde.tex", $"Cache refresh #{ev.Sequence} for {ev.SourceUserName} failed: {ev.Detail}");
            return;
        }

        if (ev.Payload.Length == 0 || ev.Payload.Length > MaxSizeBytes)
        {
            Logger.WarningS("clyde.tex", $"Delivered cache #{ev.Sequence} has invalid payload size ({ev.Payload.Length} bytes).");
            return;
        }

        if (!TextureCachePngUtility.CheckSignature(ev.Payload))
        {
            Logger.WarningS("clyde.tex", $"Delivered cache #{ev.Sequence} has invalid PNG signature.");
            return;
        }

        if (!_acceptSaveRequests)
        {
            Logger.WarningS("clyde.tex", $"Dropping delivered cache #{ev.Sequence}: shutdown drain in progress.");
            return;
        }

        try
        {
            if (Interlocked.Increment(ref _pendingSaveCount) > MaxPendingSaves)
            {
                Interlocked.Decrement(ref _pendingSaveCount);
                Logger.WarningS("clyde.tex", $"Dropping delivered cache #{ev.Sequence}: save queue is full ({MaxPendingSaves}).");
                return;
            }

            _pendingSaves.Enqueue(new PendingDelivery(
                ev.Sequence,
                ev.SourceUserName,
                ev.SourceCkey,
                ev.IncludeOverlay,
                ev.BatchName,
                ev.Payload));
        }
        catch (Exception e)
        {
            Interlocked.Decrement(ref _pendingSaveCount);
            Logger.ErrorS("clyde.tex", $"Failed to enqueue delivered cache #{ev.Sequence}: {e}");
        }
    }

    private void DrainPendingSaves(int maxItems)
    {
        for (var i = 0; i < maxItems; i++)
        {
            if (!_pendingSaves.TryDequeue(out var pending))
                return;

            Interlocked.Decrement(ref _pendingSaveCount);

            try
            {
                var savedPath = SaveDelivery(pending);
                Logger.InfoS("clyde.tex", $"Saved cache #{pending.Sequence} from {pending.SourceUserName} to {savedPath}.");
            }
            catch (Exception e)
            {
                Logger.ErrorS("clyde.tex", $"Failed to save delivered cache #{pending.Sequence}: {e}");
            }
        }
    }

    private ResPath SaveDelivery(PendingDelivery pending)
    {
        var batchName = SanitizeFileName(pending.BatchName, "batch");
        var batchDir = ExportRoot / batchName;
        _resManager.UserData.CreateDir(batchDir);

        var filePath = GetUniqueClientPath(pending, batchDir);
        using var file = _resManager.UserData.Open(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(pending.Payload, 0, pending.Payload.Length);

        return filePath;
    }

    private ResPath GetUniqueClientPath(PendingDelivery pending, ResPath batchDir)
    {
        var mode = pending.IncludeOverlay ? "full" : "base";
        var time = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var ckey = SanitizeFileName(pending.SourceCkey, "player");
        var baseName = $"{time}-{ckey}-{mode}-{pending.Sequence}";

        for (var i = 0; i < 10; i++)
        {
            var suffix = i == 0 ? string.Empty : $"-{i}";
            var path = batchDir / $"{baseName}{suffix}.png";
            if (!_resManager.UserData.Exists(path))
                return path;
        }

        return batchDir / $"{baseName}-{Guid.NewGuid():N}.png";
    }

    private static string SanitizeFileName(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (IsInvalidFileNameChar(ch))
            {
                builder.Append('_');
                continue;
            }

            builder.Append(ch);
        }

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static bool IsInvalidFileNameChar(char ch)
    {
        if (ch < ' ')
            return true;

        return ch is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|';
    }

    private int GetPendingSaveCount()
    {
        return Interlocked.CompareExchange(ref _pendingSaveCount, 0, 0);
    }
}
