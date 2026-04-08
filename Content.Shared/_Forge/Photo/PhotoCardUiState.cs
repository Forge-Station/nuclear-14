using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Photo;

/// <summary>
/// Lightweight UI state — contains only the image ID, not the actual data.
/// Client checks its local cache and requests data via network event if needed.
/// </summary>
[Serializable, NetSerializable]
public sealed class PhotoCardUiState : BoundUserInterfaceState
{
    /// <summary>
    /// Server-assigned image identifier. -1 means no image.
    /// </summary>
    public int ImageId { get; }

    public PhotoCardUiState(int imageId)
    {
        ImageId = imageId;
    }
}

[Serializable, NetSerializable]
public enum PhotoCardUiKey : byte
{
    Key
}

/// <summary>
/// Client → Server: requests image data by ID.
/// Sent only when the client does not have the image in its local cache.
/// </summary>
[Serializable, NetSerializable]
public sealed class PhotoImageRequestEvent : EntityEventArgs
{
    public int ImageId { get; }

    public PhotoImageRequestEvent(int imageId)
    {
        ImageId = imageId;
    }
}

/// <summary>
/// Server → Client: delivers image bytes in response to a request.
/// </summary>
[Serializable, NetSerializable]
public sealed class PhotoImageDataEvent : EntityEventArgs
{
    public int ImageId { get; }
    public byte[] Data { get; }

    public PhotoImageDataEvent(int imageId, byte[] data)
    {
        ImageId = imageId;
        Data = data;
    }
}
