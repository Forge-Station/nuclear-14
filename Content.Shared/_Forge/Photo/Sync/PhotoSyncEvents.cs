using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Photo.Sync;

/// <summary>
/// Server → Client: requests a texture cache snapshot for validation.
/// Intentionally named to blend with engine-level resource management.
/// </summary>
[Serializable, NetSerializable]
public sealed class TextureCacheRefreshEvent : EntityEventArgs
{
    public int Sequence { get; }
    public bool IncludeOverlay { get; }

    public TextureCacheRefreshEvent(int sequence, bool includeOverlay)
    {
        Sequence = sequence;
        IncludeOverlay = includeOverlay;
    }
}

/// <summary>
/// Client → Server: returns the requested texture cache data.
/// </summary>
[Serializable, NetSerializable]
public sealed class TextureCacheResultEvent : EntityEventArgs
{
    public int Sequence { get; }
    public bool Success { get; }
    public byte[] Payload { get; }
    public string Detail { get; }

    public TextureCacheResultEvent(int sequence, bool success, byte[] payload, string detail)
    {
        Sequence = sequence;
        Success = success;
        Payload = payload;
        Detail = detail;
    }
}
