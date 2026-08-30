using Content.Shared.Audio.Jukebox;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Nuclear14.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class PipBoyRadioUiMessageEvent : CartridgeMessageEvent
{
    public readonly PipBoyRadioAction Action;
    public readonly ProtoId<JukeboxPrototype>? SongId;

    public PipBoyRadioUiMessageEvent(
        PipBoyRadioAction action,
        ProtoId<JukeboxPrototype>? songId = null)
    {
        Action = action;
        SongId = songId;
    }
}

[Serializable, NetSerializable]
public enum PipBoyRadioAction
{
    Select,
    Play,
    Pause,
    Stop,
    Previous,
    Next
}
