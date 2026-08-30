using Content.Shared.Audio.Jukebox;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Nuclear14.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class PipBoyRadioUiState : BoundUserInterfaceState
{
    public readonly ProtoId<JukeboxPrototype>? SelectedSongId;
    public readonly bool Playing;
    public readonly bool Paused;

    public PipBoyRadioUiState(
        ProtoId<JukeboxPrototype>? selectedSongId,
        bool playing,
        bool paused)
    {
        SelectedSongId = selectedSongId;
        Playing = playing;
        Paused = paused;
    }
}
