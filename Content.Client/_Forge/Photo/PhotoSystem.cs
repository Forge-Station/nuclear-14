using System.Collections.Generic;
using Content.Client._Forge.Photo.UI;
using Content.Shared._Forge.Photo;

namespace Content.Client._Forge.Photo;

public sealed partial class PhotoSystem : SharedPhotoSystem
{
    private readonly Dictionary<EntityUid, PhotoCameraBoundUserInterface> _activeCameras = new();

    #region Image Cache

    /// <summary>
    /// Client-side cache of photo card images by server-assigned ImageId.
    /// Prevents re-downloading the same image on repeated card opens.
    /// </summary>
    private readonly Dictionary<int, byte[]> _imageCache = new();

    /// <summary>
    /// Maximum cached images. Full reset when exceeded — simple and sufficient for in-round use.
    /// </summary>
    private const int MaxCacheSize = 128;

    /// <summary>
    /// Fired when image data arrives from the server.
    /// BoundUi instances subscribe to this to display the image.
    /// </summary>
    public event Action<int, byte[]>? OnImageReceived;

    public byte[]? GetCachedImage(int imageId)
    {
        return _imageCache.GetValueOrDefault(imageId);
    }

    public void CacheImage(int imageId, byte[] data)
    {
        if (_imageCache.Count >= MaxCacheSize)
            _imageCache.Clear();

        _imageCache[imageId] = data;
    }

    /// <summary>
    /// Sends a request to the server for image data.
    /// Response will arrive via PhotoImageDataEvent.
    /// </summary>
    public void RequestImage(int imageId)
    {
        RaiseNetworkEvent(new PhotoImageRequestEvent(imageId));
    }

    private void OnImageDataReceived(PhotoImageDataEvent ev, EntitySessionEventArgs _)
    {
        CacheImage(ev.ImageId, ev.Data);
        OnImageReceived?.Invoke(ev.ImageId, ev.Data);
    }

    #endregion

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<PhotoImageDataEvent>(OnImageDataReceived);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_activeCameras.Count == 0)
            return;

        List<EntityUid>? toRemove = null;

        foreach (var (uid, window) in _activeCameras)
        {
            if (!TryComp<PhotoCameraComponent>(uid, out var component))
            {
                toRemove ??= new List<EntityUid>();
                toRemove.Add(uid);
                continue;
            }

            window.UpdateControl(component, frameTime);
        }

        if (toRemove != null)
        {
            foreach (var uid in toRemove)
                _activeCameras.Remove(uid);
        }
    }

    public void OpenCameraUi(EntityUid uid, PhotoCameraBoundUserInterface window)
    {
        _activeCameras.TryAdd(uid, window);
    }

    public void CloseCameraUi(EntityUid uid)
    {
        _activeCameras.Remove(uid);
    }
}
