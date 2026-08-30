using Content.Shared.Audio.Jukebox;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

[RegisterComponent]
public sealed partial class PipBoyRadioCartridgeComponent : Component
{
    [DataField]
    public ProtoId<JukeboxPrototype>? SelectedSongId;

    [ViewVariables]
    public EntityUid? AudioStream;

    [ViewVariables]
    public EntityUid? LoaderUid;

    [ViewVariables]
    public EntityUid? ListenerUid;
    [ViewVariables]
    public bool Playing;

    [ViewVariables]
    public bool Paused;


    [ViewVariables]
    public float PlaybackGrace;
}
