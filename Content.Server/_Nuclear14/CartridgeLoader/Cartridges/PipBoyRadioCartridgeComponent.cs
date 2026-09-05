using Content.Shared.Audio.Jukebox;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

[RegisterComponent]
public sealed partial class PipBoyRadioCartridgeComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<JukeboxPrototype>> Songs = new();

    [DataField]
    public ProtoId<JukeboxPrototype>? SelectedSongId;

    [ViewVariables]
    public EntityUid? AudioStream;

    [ViewVariables]
    public EntityUid? ListenerUid;

    [ViewVariables]
    public EntityUid? WearerUid;
    [ViewVariables]
    public bool Playing;

    [ViewVariables]
    public bool Paused;


    [ViewVariables]
    public float PlaybackGrace;
}
