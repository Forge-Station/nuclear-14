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
        proofPrototype = contract.Config.ProofPrototype;
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
        _objectiveRuntimeByProof[proof] = key;
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

        if (!TryGetObjectiveProofPrototype(contract, out var proofPrototype))
            return true;

        var key = (store, contractId);
        if (!_objectiveRuntimeByContract.TryGetValue(key, out var state) ||
            (string.IsNullOrWhiteSpace(state.ProofToken) && !CanUseTrackedProofEntityFallback(contract, state)))
        {
            fail = ClaimAttemptResult.Fail(
                ClaimFailureReason.MissingProof,
                $"Contract '{contractId}' requires a proof item, but no proof token is registered.");
            return false;
        }

        if (!TryFindObjectiveProofEntity(store, user, key, state, proofPrototype, out var proof))
        {
            LogObjectiveProofClaimDebug(store, user, key, state, proofPrototype);
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

        // Fix (B39): remove from the proof-index BEFORE Del(). Otherwise the subsequent
        // EntityTerminatingEvent would look it up and fail the contract we are actively claiming.
        _objectiveRuntimeByProof.Remove(proof);

        if (EntityManager.EntityExists(proof))
            Del(proof);

        return true;
    }

    private bool TryFindObjectiveProofEntity(
        EntityUid store,
        EntityUid user,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        string proofPrototype,
        out EntityUid proof)
    {
        var userItems = new List<EntityUid>(32);
        _logic.ScanInventoryItems(user, userItems);
        if (TryFindObjectiveProofInSource(user, userItems, key, state, proofPrototype, out proof))
            return true;

        var crateUid = _logic.GetPulledClosedCrate(user);
        if (crateUid is { } pulledCrate && Exists(pulledCrate))
        {
            var crateItems = new List<EntityUid>(32);
            _logic.ScanInventoryItems(pulledCrate, crateItems);
            if (TryFindObjectiveProofInSource(pulledCrate, crateItems, key, state, proofPrototype, out proof))
                return true;
        }

        return TryFindNearbyStoreObjectiveProof(store, key, state, proofPrototype, out proof);
    }

    private bool TryFindObjectiveProofInSource(
        EntityUid root,
        IReadOnlyList<EntityUid> items,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        string proofPrototype,
        out EntityUid proof)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var ent = items[i];
            if (!CanUseContractPlanningEntity(root, ent, worldTurnInSource: false))
                continue;

            if (IsMatchingObjectiveProof(ent, key, state, proofPrototype, out _))
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
        ObjectiveRuntimeState state,
        string proofPrototype,
        out EntityUid proof)
    {
        foreach (var ent in _lookup.GetEntitiesInRange(
                     store,
                     NcContractTuning.TrackedDeliveryStoreRange,
                     LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (!CanUseNearbyStoreObjectiveProofEntity(store, ent))
                continue;

            if (IsMatchingObjectiveProof(ent, key, state, proofPrototype, out _))
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
        ObjectiveRuntimeState state,
        string proofPrototype,
        out string rejectReason)
    {
        if (!TryComp(ent, out NcContractProofComponent? proofComp))
        {
            rejectReason = "missing component";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(proofPrototype) &&
            (!TryGetPlanningEntityPrototypeId(ent, out var candidatePrototype) ||
             !string.Equals(candidatePrototype, proofPrototype, StringComparison.Ordinal)))
        {
            rejectReason = "wrong prototype";
            return false;
        }

        if (proofComp.Store != key.Store)
        {
            rejectReason = "wrong store";
            return false;
        }

        if (!string.Equals(proofComp.ContractId, key.ContractId, StringComparison.Ordinal))
        {
            rejectReason = "wrong contract id";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(state.ProofToken) &&
            string.Equals(proofComp.ProofToken, state.ProofToken, StringComparison.Ordinal))
        {
            rejectReason = string.Empty;
            return true;
        }

        if (CanUseTrackedProofEntityFallback(key, state, ent))
        {
            rejectReason = string.Empty;
            return true;
        }

        rejectReason = "wrong proof token";
        return false;
    }

    private static bool CanUseTrackedProofEntityFallback(
        ContractServerData contract,
        ObjectiveRuntimeState state)
    {
        return contract.Config.HuntV2Enabled &&
               state.ProofEntity is { } proof &&
               proof != EntityUid.Invalid;
    }

    private static bool CanUseTrackedProofEntityFallback(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid ent)
    {
        return state.HuntV2Active &&
               state.ProofEntity == ent;
    }

    private void LogObjectiveProofClaimDebug(
        EntityUid store,
        EntityUid user,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        string proofPrototype)
    {
        var scanned = new List<EntityUid>(32);
        _logic.ScanInventoryItems(user, scanned);

        var candidates = 0;
        for (var i = 0; i < scanned.Count; i++)
        {
            var ent = scanned[i];
            if (!TryComp(ent, out NcContractProofComponent? _))
                continue;

            candidates++;
            IsMatchingObjectiveProof(ent, key, state, proofPrototype, out var reason);
            Sawmill.Info(
                $"[Contracts] Proof candidate rejected for '{key.ContractId}' in user inventory: " +
                $"{ToPrettyString(ent)}, reason='{reason}', expectedProto='{proofPrototype}'.");
        }

        Sawmill.Info(
            $"[Contracts] Proof claim debug for '{key.ContractId}' on {ToPrettyString(store)} by {ToPrettyString(user)}: " +
            $"expectedProto='{proofPrototype}', tokenRegistered={!string.IsNullOrWhiteSpace(state.ProofToken)}, " +
            $"trackedProof='{state.ProofEntity}', candidatesInInventory={candidates}.");
    }
}
