using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Rendering.Cache;

/// <summary>
/// Periodic texture cache consistency check.
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
/// Response to a cache refresh request.
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
