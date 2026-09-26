using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Nuclear14.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class PipBoyReceiverUiState(
    List<ProtoId<RadioChannelPrototype>> channels,
    string selectedChannel,
    bool enabled) : BoundUserInterfaceState
{
    public readonly List<ProtoId<RadioChannelPrototype>> Channels = channels;
    public readonly string SelectedChannel = selectedChannel;
    public readonly bool Enabled = enabled;
}
