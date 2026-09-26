using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

[RegisterComponent]
public sealed partial class PipBoyReceiverCartridgeComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<RadioChannelPrototype>> Channels = new();
}
