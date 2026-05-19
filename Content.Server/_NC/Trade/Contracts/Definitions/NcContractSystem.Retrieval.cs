using System;
using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private ContractServerData CreateRetrievalContractData(EntityUid store, NcRetrievalContractPrototype proto)
    {
        var cargo = BuildRetrievalCargoTargets(store, proto);
        var totalRequired = CalculateTotalRequired(cargo);
        var mainTarget = GetPrimaryTargetId(cargo);
        var matchMode = cargo.Count > 0 ? cargo[0].MatchMode : PrototypeMatchMode.Exact;
        var rewards = BakeRewardsForContract(store, proto.ID, BuildRetrievalRewardDefs(store, proto));
        var config = CreateRetrievalObjectiveConfig(proto);

        var contract = new ContractServerData
        {
            Id = proto.ID,
            Name = proto.Name,
            Description = proto.Description,
            Repeatable = proto.Repeatable,
            Taken = false,
            ObjectiveType = ContractObjectiveType.Delivery,
            ExecutionKind = ResolveRetrievalExecutionKind(config),
            Runtime = new ContractRuntimeContextData(),
            Config = config,
            FlowStatus = ContractFlowStatus.Available,
            MatchMode = matchMode,
            Targets = cargo,
            TargetItem = mainTarget,
            Required = totalRequired,
            Progress = 0,
            Rewards = rewards
        };

        SyncContractFlowStatus(contract);
        return contract;
    }

    private static ContractExecutionKind ResolveRetrievalExecutionKind(ContractObjectiveConfigData config)
    {
        if (IsTrackedRetrievalRouteDeliveryConfig(config))
            return ContractExecutionKind.RetrievalRouteDelivery;

        return string.IsNullOrWhiteSpace(config.TargetPrototype)
            ? ContractExecutionKind.InventoryDelivery
            : ContractExecutionKind.TrackedDeliveryObjective;
    }

    private static bool IsTrackedRetrievalRouteDeliveryConfig(ContractObjectiveConfigData config)
    {
        return !string.IsNullOrWhiteSpace(config.RetrievalRouteId) &&
               config.RetrievalSpawnEnabled &&
               config.RetrievalRequireSpawnedEntities &&
               config.RetrievalDestinationType != NcRetrievalDestinationTargetType.StoreUi;
    }

    private static bool UsesRetrievalSpawnedCargoSupport(ContractServerData contract)
    {
        var config = contract.Config;
        return (contract.IsInventoryDelivery || contract.IsRetrievalRouteDelivery) &&
               config.RetrievalSpawnEnabled &&
               config.RetrievalRequireSpawnedEntities;
    }

    private ContractObjectiveConfigData CreateRetrievalObjectiveConfig(NcRetrievalContractPrototype proto)
    {
        var config = new ContractObjectiveConfigData();

        if (!TryResolveRetrievalRoute(proto.ID, proto.Route, out var route, out var source, out var destination, out var proof, out var guidance))
            return config;

        config.RetrievalRouteId = route.ID;
        config.RetrievalClaimMode = route.Claim.Mode;
        config.RetrievalDestinationType = destination.Target.Type;
        config.RetrievalDestinationId = destination.Target.Id;
        config.RetrievalDestinationRadius = Math.Max(0.25f, destination.Radius);
        if (destination.Target.Type == NcRetrievalDestinationTargetType.StoreUi)
            config.AllowStoreWorldTurnIn = true;
        if (destination.Target.Type == NcRetrievalDestinationTargetType.MarkerGroup)
        {
            config.RetrievalDestinationPoint = new ContractPointSelectorPrototype
            {
                Type = ContractPointSelectorType.MarkerGroup,
                Id = destination.Target.Id
            };
        }
        config.RetrievalConsumeCargo = route.Delivery.ConsumeCargo;
        config.RetrievalLockDeliveredCargo = route.Delivery.LockDeliveredCargo;

        if (source != null && source.SpawnCargo)
        {
            config.RetrievalSpawnEnabled = true;
            config.RetrievalSpawnPoint = CloneContractPointSelector(source.Point);
            config.RetrievalSpawnFallbackToStore = source.FallbackToStore;
            config.RetrievalRequireSpawnedEntities = true;
        }

        if (config.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof && proof != null)
        {
            config.RetrievalProofEnabled = true;
            config.ProofPrototype = proof.Prototype;
            config.RetrievalProofConsumeOnRewardClaim = proof.ConsumeOnRewardClaim;
            config.RetrievalProofOwnership = proof.Ownership;
            config.RetrievalProofReissue = proof.Reissue;
        }

        if (guidance != null)
        {
            config.RetrievalSourceHint = guidance.SourceHint;
            config.RetrievalDestinationHint = guidance.DestinationHint;

            if (guidance.Pinpointer.Enabled)
            {
                config.RetrievalGuidancePinpointerEnabled = true;
                config.RetrievalGuidancePinpointerTarget = guidance.Pinpointer.Target;
                config.RetrievalGuidancePinpointerPrototype = guidance.Pinpointer.Prototype;
                config.RetrievalGuidanceMaxActivePinpointers = Math.Max(1, guidance.Pinpointer.MaxActive);
                config.GivePinpointer = true;
                config.PinpointerPrototype = guidance.Pinpointer.Prototype;
            }
        }

        NormalizeObjectiveConfig(config);
        return config;
    }

    private bool TryResolveRetrievalRoute(
        string contractId,
        ProtoId<NcRetrievalRoutePresetPrototype> routeId,
        out NcRetrievalRoutePresetPrototype route,
        out NcRetrievalSourcePresetPrototype? source,
        out NcRetrievalDestinationPresetPrototype destination,
        out NcRetrievalProofPresetPrototype? proof,
        out NcRetrievalGuidancePresetPrototype? guidance)
    {
        source = null;
        proof = null;
        guidance = null;
        route = default!;
        destination = default!;

        if (!_prototypes.TryIndex(routeId, out route!))
        {
            Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' references missing route preset '{routeId}'.");
            return false;
        }

        if (!_prototypes.TryIndex(route.Destination, out destination!))
        {
            Sawmill.Warning($"[Contracts] Retrieval route '{route.ID}' references missing destination preset '{route.Destination}'.");
            return false;
        }

        if (route.Source is { } sourceId && !_prototypes.TryIndex(sourceId, out source!))
        {
            Sawmill.Warning($"[Contracts] Retrieval route '{route.ID}' references missing source preset '{sourceId}'.");
            return false;
        }

        if (route.Claim.Proof is { } resolvedProofId && !_prototypes.TryIndex(resolvedProofId, out proof!))
        {
            Sawmill.Warning($"[Contracts] Retrieval route '{route.ID}' references missing proof preset '{resolvedProofId}'.");
            return false;
        }

        if (route.Guidance is { } guidanceId && !_prototypes.TryIndex(guidanceId, out guidance!))
        {
            Sawmill.Warning($"[Contracts] Retrieval route '{route.ID}' references missing guidance preset '{guidanceId}'.");
            return false;
        }

        return true;
    }

    private List<ContractTargetServerData> BuildRetrievalCargoTargets(EntityUid store, NcRetrievalContractPrototype proto)
    {
        if (proto.Cargo.Count == 0)
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{proto.ID}' has no cargo. " +
                "Use 'cargo' with at least one entry.");
            return new();
        }

        var targets = new List<ContractTargetServerData>(proto.Cargo.Count);
        for (var i = 0; i < proto.Cargo.Count; i++)
        {
            if (!TryBuildRetrievalCargoTarget(store, proto.ID, i, proto.Cargo[i], out var target))
                continue;

            targets.Add(target);
        }

        return targets;
    }

    private bool TryBuildRetrievalCargoTarget(
        EntityUid store,
        string contractId,
        int index,
        NcSupplyTargetEntry entry,
        out ContractTargetServerData target)
    {
        target = default!;

        if (!TryValidateRetrievalCargo(contractId, index, entry))
            return false;

        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);

        var required = RollFair(
            new(QuasiKeyKind.Req, store, contractId, $"retrieval-cargo:{index}:{entry.Prototype}:{entry.Group}"),
            entry.Count,
            1);

        if (required <= 0)
            return false;

        if (hasPrototype)
        {
            target = new ContractTargetServerData
            {
                TargetItem = entry.Prototype,
                Required = required,
                Progress = 0,
                MatchMode = PrototypeMatchMode.Exact
            };
            return true;
        }

        target = new ContractTargetServerData
        {
            TargetItem = entry.Group,
            Required = required,
            Progress = 0,
            MatchMode = PrototypeMatchMode.Matcher
        };
        return true;
    }

    private List<ContractRewardDef> BuildRetrievalRewardDefs(EntityUid store, NcRetrievalContractPrototype proto)
    {
        var rewards = new List<ContractRewardDef>(proto.Reward.Count);
        for (var i = 0; i < proto.Reward.Count; i++)
            TryAppendRetrievalRewardEntry(store, proto.ID, $"reward[{i}]", proto.Reward[i], rewards);

        return rewards;
    }

    private bool TryAppendRetrievalRewardEntry(
        EntityUid store,
        string contractId,
        string path,
        NcSupplyRewardEntry entry,
        List<ContractRewardDef> output)
    {
        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} does not define 'count'.");
            return false;
        }

        if (!IsRewardCountRange(entry.Count))
        {
            Sawmill.Warning(
                $"[Contracts] Retrieval contract '{contractId}' {path} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}.");
            return false;
        }

        switch (entry.Type)
        {
            case StoreRewardType.Item:
                if (string.IsNullOrWhiteSpace(entry.Prototype))
                {
                    Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} is Item but has no prototype.");
                    return false;
                }

                if (!_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                {
                    Sawmill.Warning(
                        $"[Contracts] Retrieval contract '{contractId}' {path} references missing entity prototype '{entry.Prototype}'.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Item,
                    RewardId = entry.Prototype,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Currency:
                if (string.IsNullOrWhiteSpace(entry.Currency))
                {
                    Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} is Currency but has no currency.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Currency,
                    RewardId = entry.Currency,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Pool:
                if (string.IsNullOrWhiteSpace(entry.Pool))
                {
                    Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} is Pool but has no pool id.");
                    return false;
                }

                if (!_prototypes.HasIndex<NcSupplyRewardPoolPrototype>(entry.Pool))
                {
                    Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} references missing Supply reward pool '{entry.Pool}'. Use type: ncSupplyRewardPool.");
                    return false;
                }

                output.Add(new ContractRewardDef
                {
                    Type = StoreRewardType.Pool,
                    RewardId = entry.Pool,
                    Count = entry.Count,
                    Weight = 1
                });
                return true;

            case StoreRewardType.Unspecified:
                Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} does not define 'type'.");
                return false;

            default:
                Sawmill.Warning($"[Contracts] Retrieval contract '{contractId}' {path} has unsupported reward type {entry.Type}.");
                return false;
        }
    }
}
