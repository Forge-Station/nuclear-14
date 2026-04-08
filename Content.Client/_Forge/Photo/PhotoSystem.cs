using System.Collections.Generic;
using Content.Client._Forge.Photo.UI;
using Robust.Client.ResourceManagement;
using Content.Shared._Forge.Photo;
using Robust.Shared.Utility;

namespace Content.Client._Forge.Photo;

public sealed partial class PhotoSystem : SharedPhotoSystem
{
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private static readonly ResPath[] PreloadTextures =
    {
        new("/Textures/_Corvax/Photo/window_background.png"),
        new("/Textures/_Corvax/Photo/paper_on.png"),
        new("/Textures/_Corvax/Photo/paper_off.png"),
        new("/Textures/_Corvax/Photo/button_base.png"),
        new("/Textures/_Corvax/Photo/button_highlight.png"),
        new("/Textures/_Corvax/Photo/button_pressed.png"),
    };

    private static readonly ResPath[] PreloadAudio =
    {
        new("/Audio/Items/Stamp/automatic_stamp.ogg"),
        new("/Audio/_Forge/Effects/servo_effect.ogg"),
    };

    private readonly Dictionary<EntityUid, PhotoCameraBoundUserInterface> _activeCameras = new();

    #region Image Cache

    /// <summary>
    /// Client-side LRU cache of photo card images by server-assigned ImageId.
    /// Prevents re-downloading the same image on repeated card opens.
    /// Uses a node dictionary for O(1) promotion and eviction.
    /// </summary>
    private readonly Dictionary<int, byte[]> _imageCache = new();
    private readonly LinkedList<int> _lruOrder = new();
    private readonly Dictionary<int, LinkedListNode<int>> _lruNodes = new();

    private const int MaxCacheSize = 128;

    public event Action<int, byte[]>? OnImageReceived;

    public byte[]? GetCachedImage(int imageId)
    {
        if (!_imageCache.TryGetValue(imageId, out var data))
            return null;

        // Move to end (most recently used) - O(1).
        if (_lruNodes.TryGetValue(imageId, out var node))
        {
            _lruOrder.Remove(node);
            _lruOrder.AddLast(node);
        }

        return data;
    }

    public void CacheImage(int imageId, byte[] data)
    {
        if (_lruNodes.TryGetValue(imageId, out var existing))
        {
            _imageCache[imageId] = data;
            _lruOrder.Remove(existing);
            _lruOrder.AddLast(existing);
            return;
        }

        // Evict oldest entry if at capacity.
        while (_imageCache.Count >= MaxCacheSize && _lruOrder.First != null)
        {
            var oldest = _lruOrder.First.Value;
            _lruOrder.RemoveFirst();
            _lruNodes.Remove(oldest);
            _imageCache.Remove(oldest);
        }

        _imageCache[imageId] = data;
        var node2 = _lruOrder.AddLast(imageId);
        _lruNodes[imageId] = node2;
    }

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
        PrewarmResources();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _activeCameras.Clear();
        _imageCache.Clear();
        _lruOrder.Clear();
        _lruNodes.Clear();
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

    private void PrewarmResources()
    {
        foreach (var path in PreloadTextures)
        {
            _resourceCache.TryGetResource<TextureResource>(path, out _);
        }

        foreach (var path in PreloadAudio)
        {
            _resourceCache.TryGetResource<AudioResource>(path, out _);
        }
    }
}

