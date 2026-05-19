using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private static (string TargetItem, PrototypeMatchMode MatchMode) MakeTurnInKey(
        string targetItem,
        PrototypeMatchMode matchMode)
    {
        return (targetItem, matchMode);
    }

    private int GetTurnedInCount(
        ObjectiveRuntimeState state,
        string targetItem,
        PrototypeMatchMode matchMode)
    {
        if (string.IsNullOrWhiteSpace(targetItem))
            return 0;

        return Math.Max(0, state.TurnedInByTarget.GetValueOrDefault(MakeTurnInKey(targetItem, matchMode), 0));
    }

    private int GetTurnedInCount(
        EntityUid store,
        string contractId,
        ContractTargetServerData target)
    {
        return _objectiveRuntimeByContract.TryGetValue((store, contractId), out var state)
            ? GetTurnedInCount(state, target.TargetItem, target.MatchMode)
            : 0;
    }

    private int GetRemainingTurnInRequirement(
        EntityUid store,
        string contractId,
        ContractTargetServerData target)
    {
        var required = Math.Max(0, target.Required);
        if (required <= 0 || string.IsNullOrWhiteSpace(target.TargetItem))
            return 0;

        return Math.Max(0, required - GetTurnedInCount(store, contractId, target));
    }

    private int GetRemainingTurnInRequirement(
        EntityUid store,
        string contractId,
        ContractTargetServerData target,
        Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int> turnedInLeftByKey)
    {
        var required = Math.Max(0, target.Required);
        if (required <= 0 || string.IsNullOrWhiteSpace(target.TargetItem))
            return 0;

        var key = MakeTurnInKey(target.TargetItem, target.MatchMode);
        if (!turnedInLeftByKey.TryGetValue(key, out var turnedInLeft))
        {
            turnedInLeft = GetTurnedInCount(store, contractId, target);
            turnedInLeftByKey[key] = turnedInLeft;
        }

        var consumedByThisTarget = Math.Min(required, Math.Max(0, turnedInLeft));
        turnedInLeftByKey[key] = Math.Max(0, turnedInLeft - consumedByThisTarget);
        return Math.Max(0, required - consumedByThisTarget);
    }

    private void RecordPartialTurnInProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract,
        List<ClaimTakeEntry> takePlan)
    {
        if (takePlan.Count == 0)
            return;

        var state = GetOrCreateObjectiveRuntimeState((store, contractId));
        for (var i = 0; i < takePlan.Count; i++)
        {
            var entry = takePlan[i];
            if (string.IsNullOrWhiteSpace(entry.TargetItem) || entry.Amount <= 0)
                continue;

            var key = MakeTurnInKey(entry.TargetItem, entry.MatchMode);
            state.TurnedInByTarget[key] = SaturatingAdd(
                state.TurnedInByTarget.GetValueOrDefault(key, 0),
                entry.Amount);
        }

    }

    private void RefreshProgressAfterPartialTurnIn(ClaimContext ctx, string contractId)
    {
        _logic.ScanInventoryItems(ctx.User, _scratchUserItems);

        List<EntityUid>? crateItems = null;
        if (ctx.Crate is { } crate && Exists(crate))
        {
            _logic.ScanInventoryItems(crate, _scratchCrateItems);
            _scratchCrateItems.Add(crate);
            crateItems = _scratchCrateItems;
        }
        else
        {
            _scratchCrateItems.Clear();
        }

        if (ctx.Contract.AllowsStoreWorldTurnIn)
            ScanStoreNearbyTurnInItems(ctx.Store, _scratchStoreNearbyItems);
        else
            _scratchStoreNearbyItems.Clear();

        if (TryUpdateRetrievalSpawnedProgress(
                ctx.Store,
                contractId,
                ctx.Contract,
                ctx.User,
                _scratchUserItems,
                ctx.Crate,
                crateItems,
                _scratchStoreNearbyItems,
                crateItems is { Count: > 0 }))
        {
            ApplyPartialTurnInProgress(ctx.Store, contractId, ctx.Contract);
            return;
        }

        UpdateContractProgressForSingleContract(
            ctx.Contract,
            ctx.Store,
            ctx.User,
            _scratchUserItems,
            ctx.Crate,
            crateItems,
            _scratchStoreNearbyItems,
            crateItems is { Count: > 0 });
        ApplyPartialTurnInProgress(ctx.Store, contractId, ctx.Contract);
    }

    private void ApplyPartialTurnInProgress(
        EntityUid store,
        string contractId,
        ContractServerData contract)
    {
        if (!_objectiveRuntimeByContract.TryGetValue((store, contractId), out var state) ||
            state.TurnedInByTarget.Count == 0)
        {
            return;
        }

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
            return;

        var turnedInLeftByKey = new Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int>();
        var totalRequired = 0;
        var totalProgress = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var required = Math.Max(0, target.Required);
            totalRequired = SaturatingAdd(totalRequired, required);

            if (required <= 0 || string.IsNullOrWhiteSpace(target.TargetItem))
            {
                target.Progress = 0;
                targets[i] = target;
                continue;
            }

            var key = MakeTurnInKey(target.TargetItem, target.MatchMode);
            if (!turnedInLeftByKey.TryGetValue(key, out var turnedIn))
            {
                turnedIn = GetTurnedInCount(state, target.TargetItem, target.MatchMode);
                turnedInLeftByKey[key] = turnedIn;
            }

            turnedIn = Math.Min(required, Math.Max(0, turnedIn));
            target.Progress = Math.Min(required, SaturatingAdd(turnedIn, Math.Max(0, target.Progress)));
            targets[i] = target;
            turnedInLeftByKey[key] = Math.Max(0, turnedInLeftByKey[key] - turnedIn);
            totalProgress = SaturatingAdd(totalProgress, target.Progress);
        }

        contract.Required = totalRequired;
        contract.Progress = Math.Min(totalRequired, totalProgress);
        if (targets.Count > 0)
            contract.TargetItem = targets[0].TargetItem;

        SyncContractFlowStatus(contract);
    }

    public bool CanPartiallyTurnInNow(
        EntityUid store,
        string contractId,
        ContractServerData contract)
    {
        if (!contract.IsInventoryDelivery ||
            !contract.Taken ||
            contract.Completed ||
            contract.FlowStatus != ContractFlowStatus.InProgress)
        {
            return false;
        }

        var requiredTotal = CalculateTotalRequired(GetEffectiveTargets(contract));
        if (requiredTotal <= 0 ||
            contract.Progress <= 0 ||
            contract.Progress >= requiredTotal)
        {
            return false;
        }

        if (!_objectiveRuntimeByContract.TryGetValue((store, contractId), out var state) ||
            state.TurnedInByTarget.Count == 0)
        {
            return true;
        }

        var turnedInProgress = CalculateAppliedTurnedInProgress(contract, state);
        return contract.Progress > turnedInProgress;
    }

    private int CalculateAppliedTurnedInProgress(
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0 || state.TurnedInByTarget.Count == 0)
            return 0;

        var turnedInLeftByKey = new Dictionary<(string TargetItem, PrototypeMatchMode MatchMode), int>();
        var totalProgress = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var required = Math.Max(0, target.Required);
            if (required <= 0 || string.IsNullOrWhiteSpace(target.TargetItem))
                continue;

            var key = MakeTurnInKey(target.TargetItem, target.MatchMode);
            if (!turnedInLeftByKey.TryGetValue(key, out var turnedInLeft))
            {
                turnedInLeft = GetTurnedInCount(state, target.TargetItem, target.MatchMode);
                turnedInLeftByKey[key] = turnedInLeft;
            }

            var progress = Math.Min(required, Math.Max(0, turnedInLeft));
            turnedInLeftByKey[key] = Math.Max(0, turnedInLeft - progress);
            totalProgress = SaturatingAdd(totalProgress, progress);
        }

        return totalProgress;
    }

    private void RetargetContractPinpointersAfterTurnIn(
        EntityUid store,
        string contractId,
        ContractServerData contract)
    {
        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return;

        if (TryResolveRetrievalRouteReturnPinpointerTarget(store, contract, state, out var returnTarget))
        {
            RetargetObjectivePinpointers(key, state, returnTarget);
            return;
        }

        if (TryResolveRetrievalSpawnedPinpointerTarget(store, contract, state, out var cargoTarget))
            RetargetObjectivePinpointers(key, state, cargoTarget);
    }
}
