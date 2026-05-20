using Content.Shared._NC.Trade;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TrySpawnObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractObjectiveConfigData config,
        EntityCoordinates spawnCoords
    )
    {
        if (!CanIssueContractPinpointer(key, state, config))
        {
            var limit = GetContractPinpointerLimit(config);
            Sawmill.Info(
                $"[Contracts] Objective init blocked for '{key.ContractId}': contract pinpointer limit reached ({limit}).");
            return false;
        }

        if (!TryResolveObjectivePinpointerPrototype(config, out var pinpointerProtoId))
            return false;

        var pinpointerCoords = ResolveObjectivePinpointerSpawnCoordinates(user, spawnCoords);
        if (!TrySpawnObjectivePinpointerEntity(key, pinpointerProtoId, pinpointerCoords, out var pinpointer))
            return false;

        RegisterObjectivePinpointer(user, target, key, state, pinpointer);
        return true;
    }

    private bool TryResolveObjectivePinpointerPrototype(
        ContractObjectiveConfigData config,
        out string pinpointerProtoId)
    {
        pinpointerProtoId = ResolvePinpointerPrototypeId(config.PinpointerPrototype);
        if (_prototypes.HasIndex<EntityPrototype>(pinpointerProtoId))
            return true;

        Sawmill.Warning(
            $"[Contracts] Objective init: pinpointer proto '{pinpointerProtoId}' not found, fallback to {NcContractTuning.DefaultContractPinpointerPrototypeId}.");
        pinpointerProtoId = NcContractTuning.DefaultContractPinpointerPrototypeId;
        return _prototypes.HasIndex<EntityPrototype>(pinpointerProtoId);
    }

    private EntityCoordinates ResolveObjectivePinpointerSpawnCoordinates(EntityUid user, EntityCoordinates fallbackCoords)
    {
        if (TryComp(user, out TransformComponent? userXform))
            return userXform.Coordinates;

        return fallbackCoords;
    }

    private bool TrySpawnObjectivePinpointerEntity(
        (EntityUid Store, string ContractId) key,
        string pinpointerProtoId,
        EntityCoordinates pinpointerCoords,
        out EntityUid pinpointer)
    {
        try
        {
            pinpointer = Spawn(pinpointerProtoId, pinpointerCoords);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Error(
                $"[Contracts] Objective init failed for '{key.ContractId}': cannot spawn pinpointer '{pinpointerProtoId}': {e}");
            pinpointer = EntityUid.Invalid;
            return false;
        }
    }

    private void RegisterObjectivePinpointer(
        EntityUid user,
        EntityUid target,
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        EntityUid pinpointer)
    {
        _pinpointer.SetTarget(pinpointer, target);
        _pinpointer.SetActive(pinpointer, true);
        _pinpointerService.RegisterIssuedPinpointer(_objectiveRuntime, key, state, user, pinpointer);
        _logic.QueuePickupToHandsOrCrateNextTick(user, pinpointer);
    }
}
