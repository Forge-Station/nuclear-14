using System;
using System.IO;
using System.Threading.Tasks;
using Content.Client.Viewport;
using Content.Shared._Forge.Photo.Sync;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Shared.Asynchronous;
using Robust.Shared.Log;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Photo.Sync;

public sealed class PhotoSyncSystem : EntitySystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IStateManager _state = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<PhotoFrameRequestEvent>(OnRequestFrame);
    }

    private void OnRequestFrame(PhotoFrameRequestEvent ev, EntitySessionEventArgs _)
    {
        try
        {
            if (ev.IncludeUi)
            {
                _clyde.Screenshot(ScreenshotType.Final, image => EncodeAndSend(ev.RequestId, image));
                return;
            }

            if (_state.CurrentState is not IMainViewportState state)
            {
                RaiseNetworkEvent(new PhotoFrameResponseEvent(
                    ev.RequestId,
                    false,
                    Array.Empty<byte>(),
                    "Cannot take no-UI frame: current state is not gameplay."));
                return;
            }

            state.Viewport.Viewport.Screenshot(image => EncodeAndSend(ev.RequestId, image));
        }
        catch (Exception e)
        {
            Logger.ErrorS("photo.capture", $"Failed to capture frame: {e}");
            RaiseNetworkEvent(new PhotoFrameResponseEvent(ev.RequestId, false, Array.Empty<byte>(), e.Message));
        }
    }

    private void EncodeAndSend<T>(int requestId, Image<T> screenshot) where T : unmanaged, IPixel<T>
    {
        _ = Task.Run(() =>
        {
            try
            {
                using var frame = screenshot;
                using var data = new MemoryStream();
                frame.SaveAsPng(data);
                var bytes = data.ToArray();

                _taskManager.RunOnMainThread(() =>
                {
                    RaiseNetworkEvent(new PhotoFrameResponseEvent(requestId, true, bytes, string.Empty));
                });
            }
            catch (Exception e)
            {
                Logger.ErrorS("photo.capture", $"Failed to encode frame: {e}");
                _taskManager.RunOnMainThread(() =>
                {
                    RaiseNetworkEvent(new PhotoFrameResponseEvent(
                        requestId,
                        false,
                        Array.Empty<byte>(),
                        e.Message));
                });
            }
        });
    }
}
