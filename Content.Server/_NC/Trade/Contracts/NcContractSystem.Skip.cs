using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    public bool TryGetContractSkipInfo(EntityUid store, NcStoreComponent comp, out string currency, out int cost)
    {
        currency = string.Empty;
        cost = 0;

        if (comp.ContractPresets.Count == 0)
            return false;

        foreach (var presetId in comp.ContractPresets)
        {
            if (string.IsNullOrWhiteSpace(presetId))
                continue;

            if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(presetId, out var preset))
                continue;

            if (preset.SkipCost <= 0)
                continue;

            cost = preset.SkipCost;

            var cur = preset.SkipCurrency;
            if (string.IsNullOrWhiteSpace(cur))
            {
                foreach (var c in comp.CurrencyWhitelist)
                {
                    if (!string.IsNullOrWhiteSpace(c))
                    {
                        cur = c;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(cur))
                return false;

            currency = cur;
            return true;
        }

        return false;
    }


    public bool TrySkipContract(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
            return false;

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
            return false;

        if (contract.Taken)
            return false;

        if (!TryGetContractSkipInfo(store, comp, out var currency, out var cost))
            return false;

        if (cost > 0 && !_logic._currency.TryTakeCurrency(user, currency, cost))
            return false;

        CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
        comp.Contracts.Remove(contractId);
        RefillContractsForStore(store, comp, contractId);
        return true;
    }
}
