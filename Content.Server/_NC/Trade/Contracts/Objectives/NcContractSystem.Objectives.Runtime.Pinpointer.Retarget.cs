using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void RetargetObjectivePinpointers(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid target)
    {
        if (target == EntityUid.Invalid || TerminatingOrDeleted(target))
            return;

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return;

        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }
    }

    private bool RetargetObjectivePinpointersForOwner(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid owner,
        EntityUid target)
    {
        if (owner == EntityUid.Invalid ||
            target == EntityUid.Invalid ||
            TerminatingOrDeleted(owner) ||
            TerminatingOrDeleted(target))
        {
            return false;
        }

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return false;

        var retargeted = false;
        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer))
                continue;

            if (!_pinpointerService.TryGetOwner(_objectiveRuntime, pinpointer, out var pinpointerOwner) ||
                pinpointerOwner != owner)
            {
                continue;
            }

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
            retargeted = true;
        }

        return retargeted;
    }

    private void RetargetRetrievalPulledCargoPinpointersForUser(EntityUid pulled, EntityUid user)
    {
        if (pulled == EntityUid.Invalid || user == EntityUid.Invalid)
            return;

        RetargetRetrievalCargoPinpointersForUser(pulled, user);

        _pinpointerService.RetrievalPulledCargoScratch.Clear();
        _logic.ScanInventoryItems(pulled, _pinpointerService.RetrievalPulledCargoScratch);
        for (var i = 0; i < _pinpointerService.RetrievalPulledCargoScratch.Count; i++)
        {
            var cargo = _pinpointerService.RetrievalPulledCargoScratch[i];
            if (cargo == pulled)
                continue;

            RetargetRetrievalCargoPinpointersForUser(cargo, user);
        }

        _pinpointerService.RetrievalPulledCargoScratch.Clear();
    }

    private bool RetargetRetrievalCargoPinpointersForUser(EntityUid cargo, EntityUid user)
    {
        if (!_objectiveRuntime.ByRetrievalCargo.TryGetValue(cargo, out var key) ||
            !_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !UsesRetrievalSpawnedPinpointerTarget(contract))
        {
            return false;
        }

        RefreshPinpointerRuntimeState(key.Store, key.ContractId, contract);
        if (contract.Runtime.Failed || !_objectiveRuntime.ByContract.TryGetValue(key, out state))
            return false;

        if (!TryResolveRetrievalRouteReturnPinpointerTargetForUser(key.Store, user, contract, state, out var target) &&
            !TryResolveRetrievalSpawnedPinpointerTargetForUser(key.Store, user, contract, state, out target))
        {
            return false;
        }

        RetargetObjectivePinpointersForOwner(key, state, user, target);
        return true;
    }

    private bool RetargetRetrievalCargoPinpointersForCurrentControllers(EntityUid cargo)
    {
        if (!_objectiveRuntime.ByRetrievalCargo.TryGetValue(cargo, out var key) ||
            !_objectiveRuntime.ByContract.TryGetValue(key, out var state) ||
            !TryGetObjectiveContract(key, out _, out var contract) ||
            !contract.Taken ||
            contract.Runtime.Failed ||
            !UsesRetrievalSpawnedPinpointerTarget(contract))
        {
            return false;
        }

        RefreshPinpointerRuntimeState(key.Store, key.ContractId, contract);
        if (contract.Runtime.Failed || !_objectiveRuntime.ByContract.TryGetValue(key, out state))
            return false;

        if (TryResolveRetrievalRouteReturnPinpointerTarget(key.Store, contract, state, out var returnTarget))
        {
            RetargetObjectivePinpointers(key, state, returnTarget);
            return true;
        }

        if (IsRetrievalCargoAlreadyAtPinpointerDestination(key.Store, contract, state, cargo) ||
            !TryResolveRetrievalCargoDestinationPinpointerTarget(key.Store, contract, state, out var target))
        {
            return false;
        }

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return false;

        var retargeted = false;
        foreach (var pinpointer in state.PinpointerEntities)
        {
            if (TerminatingOrDeleted(pinpointer) ||
                !_pinpointerService.TryGetOwner(_objectiveRuntime, pinpointer, out var owner) ||
                owner == EntityUid.Invalid ||
                !IsRetrievalCargoControlledByUser(cargo, owner))
            {
                continue;
            }

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
            retargeted = true;
        }

        return retargeted;
    }

    private void RetargetRetrievalPinpointersToCurrentStep(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        if (!contract.Config.RetrievalGuidancePinpointerEnabled ||
            !UsesRetrievalSpawnedPinpointerTarget(contract))
        {
            return;
        }

        PruneInvalidPinpointers(key, state);
        if (state.PinpointerEntities.Count == 0)
            return;

        _pinpointerService.ObjectivePinpointersScratch.Clear();
        _pinpointerService.ObjectivePinpointersScratch.AddRange(state.PinpointerEntities);

        for (var i = 0; i < _pinpointerService.ObjectivePinpointersScratch.Count; i++)
        {
            var pinpointer = _pinpointerService.ObjectivePinpointersScratch[i];
            if (TerminatingOrDeleted(pinpointer))
                continue;

            EntityUid target;
            if (_pinpointerService.TryGetOwner(_objectiveRuntime, pinpointer, out var owner) &&
                owner != EntityUid.Invalid &&
                !TerminatingOrDeleted(owner))
            {
                if (!TryResolveRetrievalRouteReturnPinpointerTargetForUser(key.Store, owner, contract, state, out target) &&
                    !TryResolveRetrievalSpawnedPinpointerTargetForUser(key.Store, owner, contract, state, out target))
                {
                    continue;
                }
            }
            else if (!TryResolveRetrievalRouteReturnPinpointerTarget(key.Store, contract, state, out target) &&
                     !TryResolveRetrievalSpawnedPinpointerTarget(key.Store, contract, state, out target))
            {
                continue;
            }

            if (target == EntityUid.Invalid || TerminatingOrDeleted(target))
                continue;

            _pinpointer.SetTarget(pinpointer, target);
            _pinpointer.SetActive(pinpointer, true);
        }

        _pinpointerService.ObjectivePinpointersScratch.Clear();
    }
}
