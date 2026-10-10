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
    /// Forge-Change
    public readonly float Volume;

    public PipBoyRadioUiMessageEvent(
        PipBoyRadioAction action,
        /// Forge-Change-Del ProtoId<JukeboxPrototype>? songId = null)
        /// Forge-Change-Start
        ProtoId<JukeboxPrototype>? songId = null,
        float volume = 1f)
        /// Forge-Change-End
    {
        Action = action;
        SongId = songId;
        /// Forge-Change
        Volume = volume;
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
    /// Forge-Change-Del Next
    /// Forge-Change-Start
    Next,
    SetVolume
    /// Forge-Change-End
}
