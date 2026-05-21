using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public void UpdateContractsProgress(
        EntityUid store,
        NcStoreComponent comp,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems
    )
    {
        if (comp.Contracts.Count == 0)
            return;

        if (!_storesUpdatingProgress.Add(store))
        {
            Sawmill.Warning(
                $"[Contracts] Re-entrant UpdateContractsProgress on {ToPrettyString(store)} skipped. " +
                "The same store is already updating progress.");
            return;
        }

        if (_progressScratchInUse)
        {
            _storesUpdatingProgress.Remove(store);
            Sawmill.Warning(
                $"[Contracts] Nested UpdateContractsProgress on {ToPrettyString(store)} skipped. " +
                "Progress scratch is already in use by another update; check event handlers.");
            return;
        }

        _progressScratchInUse = true;
        try
        {
            var storeNearbyItemsPrepared = false;
            var hasCrateWork = crate is not null && crateItems is { Count: > 0, };
            PopulateProgressContractIds(comp);

            try
            {
                for (var i = 0; i < _progressContractIdsScratch.Count; i++)
                    UpdateProgressForContract(
                        comp,
                        _progressContractIdsScratch[i],
                        store,
                        user,
                        userItems,
                        crate,
                        crateItems,
                        includeStoreWorldItems,
                        hasCrateWork,
                        ref storeNearbyItemsPrepared);
            }
            finally
            {
                _progressContractIdsScratch.Clear();
            }
        }
        finally
        {
            _progressScratchInUse = false;
            _storesUpdatingProgress.Remove(store);
        }
    }

    private void PopulateProgressContractIds(NcStoreComponent comp)
    {
        _progressContractIdsScratch.Clear();
        foreach (var contractId in comp.Contracts.Keys)
            _progressContractIdsScratch.Add(contractId);
    }

    private bool TryGetProgressContract(
        NcStoreComponent comp,
        string contractId,
        out ContractServerData contract
    )
    {
        if (!comp.Contracts.TryGetValue(contractId, out contract!))
            return false;

        if (contract.Taken)
            return true;

        ResetContractProgress(contract);
        return false;
    }

    private void UpdateProgressForContract(
        NcStoreComponent comp,
        string contractId,
        EntityUid store,
        EntityUid user,
        IReadOnlyList<EntityUid> userItems,
        EntityUid? crate,
        IReadOnlyList<EntityUid>? crateItems,
        bool includeStoreWorldItems,
        bool hasCrateWork,
        ref bool storeNearbyItemsPrepared
    )
    {
        if (!TryGetProgressContract(comp, contractId, out var contract))
            return;

        if (TryUpdateContractProgressByExecutionKind(store, contractId, contract, userItems, crateItems))
            return;

        if (TryUpdateRetrievalRouteDeliveryProgress(store, contractId, contract))
            return;

        EnsureStoreNearbyProgressItems(
            store,
            contract,
            includeStoreWorldItems,
            ref storeNearbyItemsPrepared);

        if (TryUpdateRetrievalSpawnedProgress(
            store,
            contractId,
            contract,
            user,
            userItems,
            crate,
            crateItems,
            _scratchStoreNearbyItems,
            hasCrateWork))
        {
            ApplyPartialTurnInProgress(store, contractId, contract);
            return;
        }

        UpdateContractProgressForSingleContract(
            contract,
            store,
            user,
            userItems,
            crate,
            crateItems,
            _scratchStoreNearbyItems,
            hasCrateWork);
        ApplyPartialTurnInProgress(store, contractId, contract);
    }

    private bool TryUpdateContractProgressByExecutionKind(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems
    ) =>
        TryGetObjectiveHandler(contract.ExecutionKind, out var handler) &&
        handler.TryUpdateProgress(this, store, contractId, contract, userItems, crateItems);

    private void EnsureStoreNearbyProgressItems(
        EntityUid store,
        ContractServerData contract,
        bool includeStoreWorldItems,
        ref bool storeNearbyItemsPrepared
    )
    {
        if (!includeStoreWorldItems || !contract.AllowsStoreWorldTurnIn || storeNearbyItemsPrepared)
            return;

        ScanStoreNearbyTurnInItems(store, _scratchStoreNearbyItems);
        storeNearbyItemsPrepared = true;
    }

    private static void ResetContractProgress(ContractServerData contract)
    {
        contract.Progress = 0;

        var targets = GetEffectiveTargets(contract);
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            target.Progress = 0;
            targets[i] = target;
        }

        SyncContractFlowStatus(contract);
    }
}
