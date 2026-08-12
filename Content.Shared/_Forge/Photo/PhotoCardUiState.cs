using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Photo;

/// <summary>
/// Lightweight UI state that contains only image ID, not image bytes.
/// Client checks local cache first and requests bytes only when needed.
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
/// Client to server: requests image bytes by ID.
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
/// Server to client: image bytes for the requested ID.
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
