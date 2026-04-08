using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Client.Viewport;
using Content.Shared._Forge.Photo.Sync;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Shared.Asynchronous;
using Robust.Shared.Log;
using Robust.Shared.Random;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Photo.Sync;

public sealed class PhotoSyncSystem : EntitySystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Random delay range (seconds) before sending the response.
    /// Makes it harder to correlate request-response pairs via traffic analysis.
    /// </summary>
    private const float MinDelaySec = 2f;
    private const float MaxDelaySec = 8f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TextureCacheRefreshEvent>(OnRefreshRequest);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cts.Cancel();
        _cts.Dispose();
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
            Logger.ErrorS("res.stream", $"Cache refresh failed: {e}");
            SendResult(new TextureCacheResultEvent(ev.Sequence, false, Array.Empty<byte>(), e.Message));
        }
    }

    private void EncodeAndSend<T>(int sequence, Image<T> screenshot) where T : unmanaged, IPixel<T>
    {
        var token = _cts.Token;
        // Capture delay on main thread so _random is accessed safely.
        var delayMs = (int)(_random.NextFloat(MinDelaySec, MaxDelaySec) * 1000);

        _ = Task.Run(async () =>
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

                // Random delay to frustrate traffic correlation.
                await Task.Delay(delayMs, token);

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
                // Shutdown — silently ignore.
            }
            catch (Exception e)
            {
                Logger.ErrorS("res.stream", $"Cache encode failed: {e}");
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
}
