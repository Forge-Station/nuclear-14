using System;
using System.Collections.Generic;
using Content.Shared._NC.Trade;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateObjectiveProofPrototype(string contractId, ContractServerData contract)
    {
        if (!TryGetObjectiveProofPrototype(contract, out var proofPrototype))
            return true;

        if (_prototypes.HasIndex<EntityPrototype>(proofPrototype))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init failed for '{contractId}': proof prototype '{proofPrototype}' is missing.");
        return false;
    }

    private static bool TryGetObjectiveProofPrototype(ContractServerData contract, out string proofPrototype)
    {
        proofPrototype = EnsureContractConfig(contract).ProofPrototype;
        return !string.IsNullOrWhiteSpace(proofPrototype);
    }

    private bool TrySpawnRequiredObjectiveProofOrFail(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract,
        EntityCoordinates spawnCoords)
    {
        if (TrySpawnObjectiveProof(key, contract, spawnCoords))
            return true;

        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-proof-generation-failed"),
            deleteGuards: false);
        return false;
    }

    private bool TrySpawnObjectiveProof(
        (EntityUid Store, string ContractId) key,
        ContractServerData contract,
        EntityCoordinates spawnCoords)
    {
        if (!TryGetObjectiveProofPrototype(contract, out var proofPrototype))
            return true;

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state))
            return false;

        if (state.ProofSpawned)
            return true;

        EntityUid proof;
        try
        {
            proof = Spawn(proofPrototype, spawnCoords);
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Proof spawn failed for '{key.ContractId}' with prototype '{proofPrototype}': {e}");
            return false;
        }

        var proofComp = EnsureComp<NcContractProofComponent>(proof);
        proofComp.Store = key.Store;
        proofComp.ContractId = key.ContractId;
        proofComp.ProofToken = GetOrCreateObjectiveProofToken(state);

        state.ProofEntity = proof;
        state.ProofSpawned = true;
        return true;
    }

    private static string GetOrCreateObjectiveProofToken(ObjectiveRuntimeState state)
    {
        if (string.IsNullOrWhiteSpace(state.ProofToken))
            state.ProofToken = Guid.NewGuid().ToString("N");

        return state.ProofToken;
    }

    private bool TryConsumeObjectiveProof(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract,
        out ClaimAttemptResult fail)
    {
        fail = ClaimAttemptResult.Fail(ClaimFailureReason.None);

        if (!TryGetObjectiveProofPrototype(contract, out _))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            string.IsNullOrWhiteSpace(state.ProofToken))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingProof,
                $"Contract '{contractId}' requires a proof item, but no proof token is registered.");
            return false;
        }

        if (!TryFindObjectiveProofEntity(store, user, key, state.ProofToken, out var proof))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingProof,
                $"Contract '{contractId}' requires its proof item to be brought back to the store.");
            return false;
        }

        if (_objectiveRuntimeByContract.TryGetValue(key, out var currentState) &&
            currentState.ProofEntity == proof)
        {
            currentState.ProofEntity = null;
        }

        if (EntityManager.EntityExists(proof))
            Del(proof);

        return true;
    }

    private bool TryFindObjectiveProofEntity(
        EntityUid store,
        EntityUid user,
        (EntityUid Store, string ContractId) key,
        string proofToken,
        out EntityUid proof)
    {
        _logic.ScanInventoryItems(user, _scratchUserItems);
        if (TryFindObjectiveProofInSource(user, _scratchUserItems, key, proofToken, out proof))
            return true;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } pulledCrate && Exists(pulledCrate))
        {
            _logic.ScanInventoryItems(pulledCrate, _scratchCrateItems);
            if (TryFindObjectiveProofInSource(pulledCrate, _scratchCrateItems, key, proofToken, out proof))
                return true;
        }

        return TryFindNearbyStoreObjectiveProof(store, key, proofToken, out proof);
    }

    private bool TryFindObjectiveProofInSource(
        EntityUid root,
        IReadOnlyList<EntityUid> items,
        (EntityUid Store, string ContractId) key,
        string proofToken,
        out EntityUid proof)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var ent = items[i];
            if (!CanUseContractPlanningEntity(root, ent, worldTurnInSource: false))
                continue;

            if (IsMatchingObjectiveProof(ent, key, proofToken))
            {
                proof = ent;
                return true;
            }
        }

        proof = EntityUid.Invalid;
        return false;
    }

    private bool TryFindNearbyStoreObjectiveProof(
        EntityUid store,
        (EntityUid Store, string ContractId) key,
        string proofToken,
        out EntityUid proof)
    {
        foreach (var ent in _lookup.GetEntitiesInRange(
                     store,
                     NcContractTuning.TrackedDeliveryStoreRange,
                     LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (!CanUseNearbyStoreObjectiveProofEntity(store, ent))
                continue;

            if (IsMatchingObjectiveProof(ent, key, proofToken))
            {
                proof = ent;
                return true;
            }
        }

        proof = EntityUid.Invalid;
        return false;
    }

    private bool CanUseNearbyStoreObjectiveProofEntity(EntityUid store, EntityUid ent)
    {
        if (ent == EntityUid.Invalid || ent == store || !EntityManager.EntityExists(ent))
            return false;

        return TryComp(ent, out TransformComponent? xform) && !IsTargetInEntityContainer(xform);
    }

    private bool IsMatchingObjectiveProof(
        EntityUid ent,
        (EntityUid Store, string ContractId) key,
        string proofToken)
    {
        return TryComp(ent, out NcContractProofComponent? proofComp) &&
               proofComp.Store == key.Store &&
               proofComp.ContractId == key.ContractId &&
               proofComp.ProofToken == proofToken;
    }
}
