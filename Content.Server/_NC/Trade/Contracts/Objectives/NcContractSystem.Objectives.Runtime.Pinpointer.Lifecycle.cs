using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool CanIssueContractPinpointer(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config
    )
    {
        PruneInvalidPinpointers(key, state);
        return state.PinpointerEntities.Count < GetContractPinpointerLimit(config);
    }

    private static int GetContractPinpointerLimit(ContractObjectiveConfigData config)
    {
        if (config.RetrievalGuidancePinpointerEnabled && config.RetrievalGuidanceMaxActivePinpointers > 0)
            return config.RetrievalGuidanceMaxActivePinpointers;

        return NcContractTuning.MaxActiveContractPinpointers;
    }

    private void PruneInvalidPinpointers((EntityUid Store, string ContractId) key, ObjectiveRuntimeState state)
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _pinpointerService.ObjectivePinpointersScratch.Clear();
        foreach (var pinpointer in state.PinpointerEntities)
            if (TerminatingOrDeleted(pinpointer))
                _pinpointerService.ObjectivePinpointersScratch.Add(pinpointer);

        for (var i = 0; i < _pinpointerService.ObjectivePinpointersScratch.Count; i++)
            UnregisterIssuedPinpointer(_pinpointerService.ObjectivePinpointersScratch[i], key);

        _pinpointerService.ObjectivePinpointersScratch.Clear();
    }

    private void UnregisterIssuedPinpointer(EntityUid pinpointer, (EntityUid Store, string ContractId) key) =>
        _pinpointerService.UnregisterIssuedPinpointer(_objectiveRuntime, pinpointer, key);

    private void CleanupObjectivePinpointers(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state
    )
    {
        if (state.PinpointerEntities.Count == 0)
            return;

        _pinpointerService.ObjectivePinpointersScratch.Clear();
        _pinpointerService.ObjectivePinpointersScratch.AddRange(state.PinpointerEntities);

        for (var i = 0; i < _pinpointerService.ObjectivePinpointersScratch.Count; i++)
        {
            var pinpointer = _pinpointerService.ObjectivePinpointersScratch[i];
            UnregisterIssuedPinpointer(pinpointer, key);

            if (!TerminatingOrDeleted(pinpointer))
                Del(pinpointer);
        }

        state.PinpointerEntities.Clear();
        _pinpointerService.ObjectivePinpointersScratch.Clear();
    }

    private bool TryGetContainedEntityRoot(EntityUid entity, out EntityUid root)
    {
        root = EntityUid.Invalid;
        if (!TryComp(entity, out TransformComponent? xform) || !IsTargetInEntityContainer(xform))
            return false;

        var current = xform.ParentUid;
        for (var guard = 0; guard < 32; guard++)
        {
            if (current == EntityUid.Invalid)
                break;

            root = current;

            if (!TryComp(current, out TransformComponent? parentXform))
                break;

            var parent = parentXform.ParentUid;
            if (parent == EntityUid.Invalid)
                break;

            if (parentXform.MapUid is { } mapUid && parent == mapUid)
                break;

            if (parentXform.GridUid is { } gridUid && parent == gridUid)
                break;

            current = parent;
        }

        return root != EntityUid.Invalid;
    }
}
