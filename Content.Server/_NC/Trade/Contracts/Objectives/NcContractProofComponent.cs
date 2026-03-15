using Robust.Shared.GameObjects;

namespace Content.Server._NC.Trade;

[RegisterComponent]
public sealed partial class NcContractProofComponent : Component
{
    [DataField]
    public EntityUid Store;

    [DataField]
    public string ContractId = string.Empty;

    [DataField]
    public string ProofToken = string.Empty;
}
