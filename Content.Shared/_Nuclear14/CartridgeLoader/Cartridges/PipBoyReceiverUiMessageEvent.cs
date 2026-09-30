using Content.Shared.CartridgeLoader;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Nuclear14.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class PipBoyReceiverUiMessageEvent(
    ProtoId<RadioChannelPrototype>? channel = null,
    bool? enabled = null) : CartridgeMessageEvent
{
    public readonly ProtoId<RadioChannelPrototype>? Channel = channel;
    public readonly bool? Enabled = enabled;
}
