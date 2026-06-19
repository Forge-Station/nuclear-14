namespace Content.Server._NC.Trade;


[RegisterComponent]
public sealed partial class NcContractProofComponent : Component
{
    [DataField]
    public string ContractId = string.Empty;

    [DataField]
    public string ProofToken = string.Empty;

    public EntityUid Store;
}
