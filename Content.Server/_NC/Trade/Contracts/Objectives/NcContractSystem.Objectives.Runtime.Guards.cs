using System.Numerics;
using Content.Shared._NC.Trade;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TrySpawnObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords
    )
    {
        if (!TryValidateObjectiveGuards(key, config, out var guardCount, out var guardPrototype))
            return guardCount >= 0;

        for (var i = 0; i < guardCount; i++)
        {
            var guardCoords = GetObjectiveGuardSpawnCoordinates(spawnCoords, i);
            if (!TrySpawnObjectiveGuard(key, state, guardPrototype, guardCoords))
                return false;
        }

        return true;
    }

    private bool TryValidateObjectiveGuards(
        (EntityUid Store, string ContractId) key,
        ContractObjectiveConfigData config,
        out int guardCount,
        out string guardPrototype)
    {
        guardCount = Math.Max(0, config.GuardCount);
        guardPrototype = config.GuardPrototype;
        if (guardCount <= 0 || string.IsNullOrWhiteSpace(guardPrototype))
            return false;

        if (_prototypes.HasIndex<EntityPrototype>(guardPrototype))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init failed for '{key.ContractId}': guard prototype '{guardPrototype}' is missing.");
        guardCount = -1;
        return false;
    }

    private EntityCoordinates GetObjectiveGuardSpawnCoordinates(EntityCoordinates spawnCoords, int index)
    {
        var baseOffset = NcContractTuning.HuntGuardSpawnOffsets[index % NcContractTuning.HuntGuardSpawnOffsets.Length];
        var ring = index / NcContractTuning.HuntGuardSpawnOffsets.Length;
        var ringScale = 1f + ring * NcContractTuning.GuardSpawnRingScaleStep;
        var jitter = new Vector2(
            (_random.NextFloat() - 0.5f) * NcContractTuning.GuardSpawnJitterScale,
            (_random.NextFloat() - 0.5f) * NcContractTuning.GuardSpawnJitterScale);
        return spawnCoords.Offset(baseOffset * ringScale + jitter);
    }

    private bool TrySpawnObjectiveGuard(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        string guardPrototype,
        EntityCoordinates guardCoords)
    {
        try
        {
            var guard = Spawn(guardPrototype, guardCoords);
            state.GuardEntities.Add(guard);
            _objectiveRuntime.ByGuard[guard] = key;
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn guard '{guardPrototype}': {e}");
            return false;
        }
    }
}
