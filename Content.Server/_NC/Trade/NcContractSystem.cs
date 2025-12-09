using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed class NcContractSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");

    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (comp.Contracts.Count > 0)
            return;

        if (!TryGetPreset(uid, comp, out var preset))
            return;

        comp.Contracts.Clear();
        AddMissingContractsFromPreset(uid, comp, preset!, false);
    }

    public bool TryClaim(EntityUid store, EntityUid user, string contractId)
    {
        if (!TryComp(store, out NcStoreComponent? comp))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no NcStoreComponent.");
            return false;
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            Sawmill.Warning($"[Claim] Contract '{contractId}' not found on {ToPrettyString(store)}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(contract.TargetItem) || contract.Required <= 0)
        {
            Sawmill.Warning(
                $"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has invalid TargetItem/Required.");
            return false;
        }

        EntityUid? crate = null;
        if (TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulled &&
            TryComp(pulled, out EntityStorageComponent? storage) &&
            !storage.Open)
            crate = pulled;

        var ownedUser = _logic.GetOwned(user, contract.TargetItem);
        var ownedCrate = crate is { } crateUid
            ? _logic.GetOwnedInRoot(crateUid, contract.TargetItem)
            : 0;

        var totalOwned = ownedUser + ownedCrate;

        if (totalOwned < contract.Required)
            return false;

        contract.Progress = Math.Min(totalOwned, contract.Required);

        var left = contract.Required;

        var takeFromUser = Math.Min(left, ownedUser);
        if (takeFromUser > 0 &&
            !_logic.TryTakeProductUnits(user, contract.TargetItem, takeFromUser))
        {
            Sawmill.Error(
                $"[Claim] Failed to take {takeFromUser}x {contract.TargetItem} from user {ToPrettyString(user)}.");
            return false;
        }

        left -= takeFromUser;

        if (left > 0 && crate is { } crateUid2)
        {
            if (!_logic.TryTakeProductUnitsFromRoot(crateUid2, contract.TargetItem, left))
            {
                Sawmill.Error(
                    $"[Claim] Failed to take {left}x {contract.TargetItem} from crate {ToPrettyString(crateUid2)}.");
                return false;
            }
        }

        if (contract.Reward > 0 && !string.IsNullOrWhiteSpace(contract.RewardCurrency))
            _logic.GiveCurrency(user, contract.RewardCurrency, contract.Reward);

        if (!string.IsNullOrWhiteSpace(contract.RewardItem) && contract.RewardItemCount > 0)
        {
            for (var i = 0; i < contract.RewardItemCount; i++)
                _logic.TrySpawnProduct(contract.RewardItem!, user);
        }

        comp.Contracts.Remove(contractId);
        RefillContractsForStore(store, comp);

        return true;
    }

    private void RefillContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (!TryGetPreset(uid, comp, out var preset))
            return;

        AddMissingContractsFromPreset(uid, comp, preset!, true);
    }

    private void AddMissingContractsFromPreset(
        EntityUid uid,
        NcStoreComponent comp,
        StoreContractsPresetPrototype preset,
        bool fillOnlyFirstMissing
    )
    {
        foreach (var contractId in preset.Contracts)
        {
            if (string.IsNullOrWhiteSpace(contractId))
                continue;

            if (comp.Contracts.ContainsKey(contractId))
                continue;

            if (!_prototypes.TryIndex<StoreContractPrototype>(contractId, out var proto))
            {
                Sawmill.Warning(
                    $"[Contracts] Contract '{contractId}' from preset '{preset.ID}' not found for {ToPrettyString(uid)}.");
                continue;
            }

            comp.Contracts[contractId] = CreateContractData(proto);

            if (fillOnlyFirstMissing)
                break;
        }
    }

    private bool TryGetPreset(EntityUid uid, NcStoreComponent comp, out StoreContractsPresetPrototype? preset)
    {
        preset = null;

        string? presetId = null;

        if (comp.ContractPresets.Count > 0)
            presetId = comp.ContractPresets[0];
        else if (!string.IsNullOrWhiteSpace(comp.LegacyContractsPreset))
            presetId = comp.LegacyContractsPreset;

        if (string.IsNullOrWhiteSpace(presetId))
            return false;

        if (!_prototypes.TryIndex<StoreContractsPresetPrototype>(presetId, out var proto))
        {
            Sawmill.Warning(
                $"[Preset] Preset '{presetId}' not found for {ToPrettyString(uid)}.");
            return false;
        }

        preset = proto;
        return true;
    }

    private static ContractServerData CreateContractData(StoreContractPrototype proto) =>
        new()
        {
            Id = proto.ID,
            Name = proto.Name,
            TargetItem = proto.TargetItem,
            Required = proto.Required,
            Progress = 0,
            Reward = proto.Reward,
            RewardCurrency = proto.RewardCurrency,
            RewardItem = proto.RewardItem,
            RewardItemCount = proto.RewardItemCount,
            Difficulty = proto.Difficulty,
            Description = proto.Description
        };
}
