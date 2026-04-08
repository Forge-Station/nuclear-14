using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Photo.Sync;

[Serializable, NetSerializable]
public sealed class PhotoFrameRequestEvent : EntityEventArgs
{
    public int RequestId { get; }
    public bool IncludeUi { get; }

    public PhotoFrameRequestEvent(int requestId, bool includeUi)
    {
        RequestId = requestId;
        IncludeUi = includeUi;
    }
}

[Serializable, NetSerializable]
public sealed class PhotoFrameResponseEvent : EntityEventArgs
{
    public int RequestId { get; }
    public bool Success { get; }
    public byte[] Data { get; }
    public string Error { get; }

    public PhotoFrameResponseEvent(int requestId, bool success, byte[] data, string error)
    {
        RequestId = requestId;
        Success = success;
        Data = data;
        Error = error;
    }
}
