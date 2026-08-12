using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void FinalizeObjectiveCompletion((EntityUid Store, string ContractId) key, ContractServerData contract)
    {
        MarkObjectiveComplete(contract);

        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            return;

        if (state.ProofEntity is { } proof && proof != EntityUid.Invalid && !TerminatingOrDeleted(proof))
        {
            RetargetObjectivePinpointers(key, state, proof);
            return;
        }

        if (RequiresSpawnedHuntBodyTurnIn(contract) && TryGetHuntBodyEntity(state, out var body))
        {
            RetargetObjectivePinpointers(key, state, body);
            return;
        }

        CleanupObjectivePinpointers(key, state);
    }

    private void FinalizeObjectiveTerminalOutcome(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        string failureReason,
        ContractObjectiveOutcome outcome = ContractObjectiveOutcome.Failed,
        bool deleteTrackedEntities = true,
        bool deleteGuards = false
    )
    {
        MarkObjectiveFailed(contract, failureReason, outcome);
        if (!contract.Repeatable &&
            contract.IsGhostRoleObjective &&
            outcome != ContractObjectiveOutcome.NotAccepted)
            comp.CompletedOneTimeContracts.Add(key.ContractId);

        if (_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            CleanupObjectivePinpointers(key, state);

        FailObjectiveContract(key, comp, deleteTrackedEntities, deleteGuards);
    }

    private void FailObjectiveContract(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        bool deleteTrackedEntities,
        bool deleteGuards
    ) =>
        RemoveObjectiveContractAndRefill(key, comp, deleteTrackedEntities, deleteGuards);

    private void RemoveObjectiveContractAndRefill(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        bool deleteTrackedEntities,
        bool deleteGuards
    )
    {
        CleanupObjectiveRuntime(key.Store, key.ContractId, deleteTrackedEntities, deleteGuards);
        comp.Contracts.Remove(key.ContractId);
        RefillContractsForStore(key.Store, comp, key.ContractId);

        var ev = new NcContractsChangedEvent();
        RaiseLocalEvent(key.Store, ref ev);
    }
}
