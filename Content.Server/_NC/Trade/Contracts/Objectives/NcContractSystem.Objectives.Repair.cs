using Content.Shared._NC.Trade;
using Content.Shared.Interaction;
using Content.Shared.Jittering;
using Robust.Shared.Audio;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryInitializeRepairObjective(
        EntityUid store,
        EntityUid user,
        string contractId,
        ContractServerData contract
    )
    {
        var config = EnsureContractConfig(contract);
        var structureProtoId = ResolveTrackedObjectivePrototypeId(config.StructurePrototype, contract.TargetItem);

        if (string.IsNullOrWhiteSpace(structureProtoId))
        {
            Sawmill.Warning($"[Contracts] Repair init failed for '{contractId}': structure prototype is missing.");
            return false;
        }

        config.StructurePrototype = structureProtoId;
        ResetObjectiveState(contract);

        if (!TryInitializeTrackedTargetAndSupport(store, user, contractId, contract, structureProtoId, false))
            return false;

        var key = (store, contractId);
        if (_objectiveRuntimeByContract.TryGetValue(key, out var state) && state.TargetEntity is { } structure)
        {
            var repair = EnsureComp<NcContractRepairObjectiveComponent>(structure);
            repair.ToolQuality = config.RepairToolQuality;
            repair.DoAfterSeconds = config.RepairDoAfterSeconds;
        }

        return true;
    }

    private void OnRepairObjectiveInteractUsing(
        EntityUid uid,
        NcContractRepairObjectiveComponent comp,
        InteractUsingEvent args
    )
    {
        if (args.Handled)
            return;

        if (!TryGetRepairRuntimeState(uid, out _, out var runtimeState))
            return;

        if (runtimeState.RepairInProgress)
        {
            args.Handled = true;
            return;
        }

        if (!TryGetActiveRepairContract(uid, out _, out _, out var contract))
            return;

        var config = EnsureContractConfig(contract);
        var quality = ResolveRepairToolQuality(
            string.IsNullOrWhiteSpace(comp.ToolQuality) ? config.RepairToolQuality : comp.ToolQuality);

        var delay = ResolveRepairDoAfterSeconds(
            comp.DoAfterSeconds > 0f ? comp.DoAfterSeconds : config.RepairDoAfterSeconds);

        runtimeState.RepairInProgress = true;

        var started = _tool.UseTool(args.Used, args.User, uid, delay, quality, new ContractRepairDoAfterEvent());
        if (!started)
            runtimeState.RepairInProgress = false;

        args.Handled = started;
    }

    private void OnRepairObjectiveDoAfter(
        EntityUid uid,
        NcContractRepairObjectiveComponent comp,
        ContractRepairDoAfterEvent args
    )
    {
        if (!TryGetRepairRuntimeState(uid, out _, out var runtimeState))
            return;

        runtimeState.RepairInProgress = false;

        if (args.Cancelled)
            return;

        if (!TryGetActiveRepairContract(uid, out var key, out var state, out var contract))
            return;

        var runtime = EnsureContractRuntime(contract);
        var config = EnsureContractConfig(contract);
        if (runtime.Stage >= Math.Max(1, runtime.StageGoal))
            return;

        SetObjectiveStage(contract, runtime.Stage + 1);

        if (runtime.Stage >= Math.Max(1, runtime.StageGoal))
        {
            if (!TryGetObjectiveContract(key, out var storeComp, out _) ||
                !TrySpawnRequiredObjectiveProofOrFail(key, storeComp, contract, Transform(uid).Coordinates))
            {
                return;
            }
        }

        PlayRepairObjectiveStageEffects(uid, config);

        if (config.GuardCount <= 0 || string.IsNullOrWhiteSpace(config.GuardPrototype))
            return;

        if (TryComp(uid, out TransformComponent? structureXform) &&
            !TrySpawnObjectiveGuards(key, state, config, structureXform.Coordinates))
            Sawmill.Warning($"[Contracts] Repair stage wave failed for '{key.ContractId}'.");
    }

    private bool TryGetRepairRuntimeState(
        EntityUid uid,
        out (EntityUid Store, string ContractId) key,
        out ObjectiveRuntimeState state
    )
    {
        key = default;
        state = default!;

        if (!_objectiveRuntimeByTarget.TryGetValue(uid, out key))
            return false;

        if (!_objectiveRuntimeByContract.TryGetValue(key, out var foundState) ||
            foundState == null ||
            foundState.TargetEntity != uid)
            return false;

        state = foundState;
        return true;
    }

    private bool TryGetActiveRepairContract(
        EntityUid uid,
        out (EntityUid Store, string ContractId) key,
        out ObjectiveRuntimeState state,
        out ContractServerData contract
    )
    {
        key = default;
        state = default!;
        contract = default!;

        if (!TryGetRepairRuntimeState(uid, out key, out state))
            return false;

        if (!TryGetObjectiveContract(key, out _, out contract))
            return false;

        if (!contract.Taken || !contract.IsRepairObjective || contract.Completed)
            return false;

        EnsureObjectiveRuntimeDefaults(contract);
        return !EnsureContractRuntime(contract).Failed;
    }

    private void PlayRepairObjectiveStageEffects(EntityUid structure, ContractObjectiveConfigData config)
    {
        var sound = ResolveRepairStageSound(config.RepairStageSound);

        _audio.PlayPvs(
            sound,
            structure,
            AudioParams.Default.WithVariation(NcContractTuning.RepairStageEffectVariation)
                .WithVolume(NcContractTuning.RepairStageEffectVolume));

        var hadJitter = HasComp<JitteringComponent>(structure);
        _jitter.AddJitter(
            structure,
            NcContractTuning.RepairStageJitterAmplitude,
            NcContractTuning.RepairStageJitterFrequency);
        if (hadJitter)
            return;

        Timer.Spawn(
            NcContractTuning.RepairStageEffectDuration,
            () =>
            {
                if (TerminatingOrDeleted(structure))
                    return;

                RemComp<JitteringComponent>(structure);
            });
    }

    private void HandleRepairObjectiveTargetResolved(
        (EntityUid Store, string ContractId) key,
        NcStoreComponent comp,
        ContractServerData contract
    )
    {
        if (contract.Completed)
        {
            FinalizeObjectiveCompletion(key, contract);
            return;
        }

        FinalizeObjectiveFailure(
            key,
            comp,
            contract,
            Loc.GetString("nc-store-contract-repair-structure-lost"),
            false);
    }
}
