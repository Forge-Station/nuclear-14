using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateRetrievalContractForPool(string packId, NcRetrievalContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[Contracts] Pack '{packId}' contains a retrieval contract with an empty prototype id.");
            return false;
        }

        if (proto.Cargo.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{proto.ID}' has no cargo. " +
                "Use 'cargo' with at least one entry. Contract skipped.");
            valid = false;
        }

        for (var i = 0; i < proto.Cargo.Count; i++)
        {
            if (!TryValidateRetrievalCargo(proto.ID, i, proto.Cargo[i]))
                valid = false;
        }

        if (!TryValidateRetrievalRoute(proto))
            valid = false;

        if (!TryValidateRetrievalRewardsForPool(proto))
            valid = false;

        if (!TryValidateContractConditions(proto.ID, proto.Conditions))
            valid = false;

        return valid;
    }
}
