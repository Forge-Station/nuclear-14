using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    public void AnalyzeContractProgressRequirements(
        NcStoreComponent comp,
        out bool hasTakenContracts,
        out bool needsUserItems,
        out bool needsCrateItems)
    {
        hasTakenContracts = false;
        needsUserItems = false;
        needsCrateItems = false;

        if (comp.Contracts.Count == 0)
            return;

        foreach (var contract in comp.Contracts.Values)
        {
            if (!contract.Taken)
                continue;

            hasTakenContracts = true;

            switch (contract.ExecutionKind)
            {
                case ContractExecutionKind.InventoryDelivery:
                case ContractExecutionKind.TrackedDeliveryObjective:
                    needsUserItems = true;
                    needsCrateItems = true;
                    return;
            }
        }
    }
}
