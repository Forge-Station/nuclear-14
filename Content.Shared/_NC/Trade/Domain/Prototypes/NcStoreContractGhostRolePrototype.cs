using Content.Shared.Customization.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Trade;

[Prototype("storeContractGhostRole")]
public sealed partial class StoreContractGhostRolePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField("entityPrototype", required: true)]
    public string EntityPrototype { get; private set; } = string.Empty;

    [DataField("name")]
    public string Name { get; private set; } = string.Empty;

    [DataField("description")]
    public string Description { get; private set; } = string.Empty;

    [DataField("rules")]
    public string Rules { get; private set; } = string.Empty;

    [DataField("requirements")]
    public List<CharacterRequirement> Requirements { get; private set; } = new();
}
