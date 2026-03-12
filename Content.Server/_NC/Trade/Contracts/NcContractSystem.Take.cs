using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryTakeContract(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return false;

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return false;

        if (contract.Taken)
            return false;

        if (!TryInitializeObjectiveRuntimeOnTake(store, user, contractId, contract))
            return false;

        contract.Taken = true;
        contract.Progress = 0;

        ResetContractTargetProgress(contract);
        SyncContractFlowStatus(contract);

        if (contract.UsesStageObjectiveProgress)
            UpdateObjectiveContractProgress(store, contractId, contract);

        return true;
    }
}
