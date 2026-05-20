using Content.Shared._NC.Trade;
using Content.Shared.Stacks;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void UpdateTrackedDeliveryDropoffObjectives()
    {
        if (_objectiveRuntime.ActiveTrackedDeliveryDropoffObjectives.Count == 0)
            return;

        _objectiveRuntime.KeysScratch.Clear();
        foreach (var key in _objectiveRuntime.ActiveTrackedDeliveryDropoffObjectives)
            _objectiveRuntime.KeysScratch.Add(key);

        for (var i = 0; i < _objectiveRuntime.KeysScratch.Count; i++)
        {
            var key = _objectiveRuntime.KeysScratch[i];
            if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
            {
                _objectiveRuntime.ActiveTrackedDeliveryDropoffObjectives.Remove(key);
                continue;
            }

            if (state.TargetEntity is not { } target ||
                target == EntityUid.Invalid ||
                TerminatingOrDeleted(target) ||
                state.DeliveryDropoffCoordinates == null)
            {
                continue;
            }

            if (!TryGetObjectiveContract(key, out _, out var contract) ||
                !contract.Taken ||
                !contract.IsTrackedDeliveryObjective ||
                !UsesTrackedDeliveryDropoff(contract) ||
                contract.Completed)
            {
                continue;
            }

            if (IsTrackedDeliveryTargetAtDropoff(target, state))
                CompleteTrackedDeliveryDropoffObjective(key);
        }

        _objectiveRuntime.KeysScratch.Clear();
    }

    private void CompleteTrackedDeliveryDropoffObjective((EntityUid Store, string ContractId) key)
    {
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            state.TargetEntity is not { } target ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(target))
        {
            return;
        }

        if (!TryGetObjectiveContract(key, out var comp, out var contract))
        {
            CleanupObjectiveRuntime(key.Store, key.ContractId, true);
            return;
        }

        if (!contract.Taken ||
            !contract.IsTrackedDeliveryObjective ||
            !UsesTrackedDeliveryDropoff(contract) ||
            contract.Completed)
        {
            return;
        }

        SetTrackedDeliveryProgress(contract, GetTrackedDeliveryAmount(contract, target));
        if (!contract.Completed)
            return;

        if (!TrySpawnRequiredObjectiveProofOrFail(key, comp, contract, Transform(target).Coordinates))
            return;

        state.DeliveryDropoffCompleted = true;

        var config = contract.Config;
        if (!config.PreserveTargetOnComplete)
        {
            _objectiveRuntime.ByTarget.Remove(target);
            state.TargetEntity = null;

            if (!TerminatingOrDeleted(target))
                Del(target);
        }

        DeactivateTrackedDeliveryDropoff(key, state);

        if (state.ProofEntity is { } proof && proof != EntityUid.Invalid && !TerminatingOrDeleted(proof))
            RetargetObjectivePinpointers(key, state, proof);
        else
            CleanupObjectivePinpointers(key, state);
    }

    private void HandleTrackedDeliveryTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract)
    {
        FinalizeObjectiveTerminalOutcome(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-delivery-target-lost"),
            deleteGuards: false);
    }

    private void UpdateTrackedDeliveryObjectiveProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems)
    {
        EnsureObjectiveRuntimeDefaults(contract);

        var key = (store, contractId);
        if (!_objectiveRuntime.ByContract.TryGetValue(key, out var state))
        {
            SetTrackedDeliveryProgress(contract, 0);
            return;
        }

        if (TryUpdateTrackedDeliveryDropoffProgress(contract, state))
            return;

        UpdateTrackedDeliveryStoreProgress(store, contract, state, userItems, crateItems);
    }

    private bool TryUpdateTrackedDeliveryDropoffProgress(
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!UsesTrackedDeliveryDropoff(contract))
            return false;

        if (state.DeliveryDropoffCompleted)
        {
            SetTrackedDeliveryProgress(contract, GetTrackedDeliveryCompletionAmount(contract));
            return true;
        }

        var target = GetLiveTrackedDeliveryObjectiveTarget(state);
        var progress = target is { } deliveryTarget && IsTrackedDeliveryTargetAtDropoff(deliveryTarget, state)
            ? GetTrackedDeliveryAmount(contract, deliveryTarget)
            : 0;

        SetTrackedDeliveryProgress(contract, progress);
        return true;
    }

    private void UpdateTrackedDeliveryStoreProgress(
        EntityUid store,
        ContractServerData contract,
        ObjectiveRuntimeState state,
        IReadOnlyList<EntityUid> userItems,
        IReadOnlyList<EntityUid>? crateItems)
    {
        var target = GetLiveTrackedDeliveryObjectiveTarget(state);
        if (target is not { } storeTarget)
        {
            SetTrackedDeliveryProgress(contract, 0);
            return;
        }

        var inUserInventory = ContainsTrackedDeliveryEntity(userItems, storeTarget);
        var inCrate = ContainsTrackedDeliveryEntity(crateItems, storeTarget);
        var atStore = IsTrackedDeliveryTargetAtStore(store, storeTarget);
        var progress = inUserInventory || inCrate || atStore
            ? GetTrackedDeliveryAmount(contract, storeTarget)
            : 0;

        SetTrackedDeliveryProgress(contract, progress);
    }

    private EntityUid? GetLiveTrackedDeliveryObjectiveTarget(ObjectiveRuntimeState state)
    {
        if (state.TargetEntity is not { } target || target == EntityUid.Invalid || TerminatingOrDeleted(target))
            return null;

        return target;
    }
}
