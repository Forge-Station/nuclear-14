using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly record struct ClaimContext(
        EntityUid Store,
        EntityUid User,
        EntityUid? Crate,
        NcStoreComponent Comp,
        ContractServerData Contract,
        List<ContractTargetServerData> Targets,
        Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> RequiredByKey,
        NcStoreLogicSystem.InventorySnapshot UserSnap,
        List<EntityUid> UserItems,
        NcStoreLogicSystem.InventorySnapshot? CrateSnap,
        List<EntityUid>? CrateItems);

    private bool TryPrepareClaimContext(EntityUid store, EntityUid user, string contractId, out ClaimContext ctx)
    {
        ctx = default;

        if (!TryComp(store, out NcStoreComponent? comp))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no NcStoreComponent.");
            return false;
        }

        if (!comp.Contracts.TryGetValue(contractId, out var contract))
        {
            Sawmill.Warning($"[Claim] Store {ToPrettyString(store)} has no contract '{contractId}'.");
            return false;
        }

        var targets = GetEffectiveTargets(contract);
        if (targets.Count == 0)
        {
            Sawmill.Warning($"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has no valid targets.");
            return false;
        }

        var requiredByKey = new Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int>();
        foreach (var t in targets)
        {
            if (string.IsNullOrWhiteSpace(t.TargetItem) || t.Required <= 0)
            {
                Sawmill.Warning($"[Claim] Contract '{contractId}' on {ToPrettyString(store)} has invalid target '{t.TargetItem}'.");
                return false;
            }

            var key = (t.TargetItem, t.MatchMode);
            if (!requiredByKey.TryAdd(key, t.Required))
                requiredByKey[key] = checked(requiredByKey[key] + t.Required);
        }

        // Build deep lists + snapshots exactly once per root.
        _logic.InvalidateInventoryCache(user);
        _logic.ScanInventory(user, _scratchUserItems, _scratchUserSnap);
        var userSnap = _scratchUserSnap;

        EntityUid? crateEntity = null;
        NcStoreLogicSystem.InventorySnapshot? crateSnap = null;
        List<EntityUid>? crateItems = null;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } c0 && Exists(c0))
        {
            crateEntity = c0;
            _logic.InvalidateInventoryCache(c0);
            _logic.ScanInventory(c0, _scratchCrateItems, _scratchCrateSnap);
            crateSnap = _scratchCrateSnap;
            crateItems = _scratchCrateItems;
        }

        // Validate sufficiency using snapshots (no mutations).
        foreach (var kvp in requiredByKey)
        {
            var (protoId, matchMode) = kvp.Key;
            var required = kvp.Value;

            var ownedUser = _logic.GetOwnedFromSnapshot(userSnap, protoId, matchMode);
            var ownedCrate = crateSnap != null ? _logic.GetOwnedFromSnapshot(crateSnap, protoId, matchMode) : 0;

            if (ownedUser + ownedCrate < required)
            {
                Sawmill.Info(
                    $"[Claim] Not enough items for '{contractId}': need {required}x {protoId} (mode={matchMode}), " +
                    $"have user={ownedUser}, crate={ownedCrate} on {ToPrettyString(store)}.");
                return false;
            }
        }

        ctx = new ClaimContext(
            store,
            user,
            crateEntity,
            comp,
            contract,
            targets,
            requiredByKey,
            userSnap,
            _scratchUserItems,
            crateSnap,
            crateItems);
        return true;
    }
}
