using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryInitializeHuntObjectiveRuntimeOnTake(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract)
    {
        if (!IsSpawnedHuntContract(contract))
            return TryInitializeHuntObjective(store, user, contractId, contract);

        return TryInitializeSpawnedHuntObjective(store, user, contractId, contract);
    }

    private static bool IsSpawnedHuntContract(ContractServerData contract)
    {
        return contract.IsHuntObjective && contract.Config.HuntEnabled;
    }

    private static bool RequiresSpawnedHuntBodyTurnIn(ContractServerData contract)
    {
        return IsSpawnedHuntContract(contract) &&
               contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn;
    }

    private bool TryInitializeSpawnedHuntObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract)
    {
        if (contract.Config.HuntCompletionMode is not (NcHuntCompletionMode.TrophyTurnIn or NcHuntCompletionMode.BodyTurnIn))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': only TrophyTurnIn and BodyTurnIn are supported.");
            return false;
        }

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.TrophyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.ProofPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': TrophyTurnIn requires proof prototype.");
            return false;
        }

        if (contract.Config.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn &&
            string.IsNullOrWhiteSpace(contract.Config.HuntBodyPrototype))
        {
            Sawmill.Warning(
                $"[Contracts] Hunt runtime init failed for '{contractId}': BodyTurnIn requires a body target.");
            return false;
        }

        var key = (store, contractId);
        var state = GetOrCreateObjectiveRuntimeState(key);
        state.TargetEntity = null;
        state.HuntBodyEntity = null;
        state.HuntSpawnedTargets.Clear();
        state.HuntTargetWasKilled = false;
        state.LastKnownTargetCoordinates = null;

        ResetObjectiveState(contract);

        if (!TrySpawnHuntTargets(store, contractId, contract, state))
        {
            CleanupObjectiveRuntime(store, contractId, deleteTrackedEntities: true);
            return false;
        }

        if (!state.HuntActive)
        {
            state.HuntActive = true;
            _objectiveRuntime.ActiveHuntObjectives.Add((store, contractId));
        }

        if (!contract.Config.GivePinpointer)
            return true;

        if (!TryResolveSpawnedHuntPinpointerTargetForUser(store, user, contract, state, out var pinpointerTarget))
            return false;

        var spawnCoords = EntityCoordinates.Invalid;
        if (TryComp(store, out TransformComponent? storeXform))
            spawnCoords = storeXform.Coordinates;
        else if (TryComp(user, out TransformComponent? userXform))
            spawnCoords = userXform.Coordinates;

        if (spawnCoords == EntityCoordinates.Invalid &&
            TryComp(pinpointerTarget, out TransformComponent? targetXform))
        {
            spawnCoords = targetXform.Coordinates;
        }

        if (spawnCoords == EntityCoordinates.Invalid)
            return false;

        return TrySpawnObjectivePinpointer(user, pinpointerTarget, key, state, contract.Config, spawnCoords);
    }
}
