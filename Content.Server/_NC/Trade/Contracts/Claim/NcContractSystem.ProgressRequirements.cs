using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public void AnalyzeContractProgressRequirements(
        NcStoreComponent comp,
        out bool hasTakenContracts,
        out bool needsUserItems,
        out bool needsCrateItems,
        out bool needsStoreWorldItems
    )
    {
        hasTakenContracts = false;
        needsUserItems = false;
        needsCrateItems = false;
        needsStoreWorldItems = false;

        if (comp.Contracts.Count == 0)
            return;

        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken)
                continue;

            hasTakenContracts = true;

            if (TryGetObjectiveHandler(contract.ExecutionKind, out var handler))
            {
                handler.AnalyzeProgressRequirements(
                    contract,
                    ref needsUserItems,
                    ref needsCrateItems,
                    ref needsStoreWorldItems);
            }
        }
    }
}
