using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Client.Viewport;
using Content.Shared._Forge.Rendering.Cache;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Shared.Asynchronous;
using Robust.Shared.Log;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Rendering.Cache;

/// <summary>
/// Handles texture cache validation requests from the server.
/// </summary>
public sealed class TextureCacheSystem : EntitySystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;

    private CancellationTokenSource _cts = new();

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
}
