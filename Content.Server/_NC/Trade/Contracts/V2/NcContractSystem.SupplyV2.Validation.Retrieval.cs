using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryValidateRetrievalContractForPool(string packId, NcRetrievalContractPrototype proto)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proto.ID))
        {
            Sawmill.Warning($"[ContractsV2] Pack '{packId}' contains a retrieval contract with an empty prototype id.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(proto.Difficulty))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{proto.ID}' has empty difficulty. Contract skipped.");
            valid = false;
        }

        if (proto.LegacyTargets.Count > 0 || proto.LegacySpawn != null || IsSupplyTargetCountConfigured(proto.LegacyTargetCount))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' uses legacy Retrieval Stage 1-4 fields. " +
                "Use cargo + route + reward. Legacy fields targets/targetCount/spawn are rejected.");
            valid = false;
        }

        if (proto.Cargo.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has no cargo. " +
                "Use 'cargo' with at least one entry. Contract skipped.");
            valid = false;
        }

        for (var i = 0; i < proto.Cargo.Count; i++)
        {
            if (!TryValidateRetrievalCargo(proto.ID, i, proto.Cargo[i]))
                valid = false;
        }

        if (!TryValidateRetrievalRoute(proto))
            valid = false;

        if (!TryValidateRetrievalRewardsForPool(proto))
            valid = false;

        return valid;
    }

    private bool TryValidateRetrievalRoute(NcRetrievalContractPrototype proto)
    {
        if (!_prototypes.TryIndex<NcRetrievalRoutePresetPrototype>(proto.Route, out var route))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{proto.ID}' references missing route preset '{proto.Route}'.");
            return false;
        }

        var valid = true;

        NcRetrievalSourcePresetPrototype? source = null;
        if (route.Source is { } sourceId)
        {
            if (!_prototypes.TryIndex(sourceId, out source))
            {
                Sawmill.Warning($"[ContractsV2] Retrieval route '{route.ID}' references missing source preset '{sourceId}'.");
                valid = false;
            }
            else if (!TryValidateRetrievalRouteSource(route.ID, source))
            {
                valid = false;
            }
        }

        if (!_prototypes.TryIndex<NcRetrievalDestinationPresetPrototype>(route.Destination, out var destination))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval route '{route.ID}' references missing destination preset '{route.Destination}'.");
            valid = false;
        }
        else if (!TryValidateRetrievalRouteDestination(route.ID, destination))
        {
            valid = false;
        }

        NcRetrievalProofPresetPrototype? proof = null;
        var proofId = ResolveRetrievalProofPresetId(route);
        if (proofId is { } resolvedProofId)
        {
            if (!_prototypes.TryIndex(resolvedProofId, out proof))
            {
                Sawmill.Warning($"[ContractsV2] Retrieval route '{route.ID}' references missing proof preset '{resolvedProofId}'.");
                valid = false;
            }
            else if (!TryValidateRetrievalRouteProof(route.ID, proof))
            {
                valid = false;
            }
        }

        if (!TryValidateRetrievalRouteClaim(route, destination, source, proof))
            valid = false;

        if (route.Guidance is { } guidanceId)
        {
            if (!_prototypes.TryIndex<NcRetrievalGuidancePresetPrototype>(guidanceId, out var guidance))
            {
                Sawmill.Warning($"[ContractsV2] Retrieval route '{route.ID}' references missing guidance preset '{guidanceId}'.");
                valid = false;
            }
            else if (!TryValidateRetrievalRouteGuidance(route.ID, guidance, source, proof))
            {
                valid = false;
            }
        }

        if (!route.Delivery.ConsumeCargo)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval route '{route.ID}' uses delivery.consumeCargo=false. " +
                "Stage 5.8R-E only supports consuming delivered cargo; persistent locked cargo is not implemented yet.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateRetrievalRouteClaim(
        NcRetrievalRoutePresetPrototype route,
        NcRetrievalDestinationPresetPrototype? destination,
        NcRetrievalSourcePresetPrototype? source,
        NcRetrievalProofPresetPrototype? proof)
    {
        var valid = true;

        if (route.Claim.Proof != null && route.Proof != null && !Equals(route.Claim.Proof, route.Proof))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval route '{route.ID}' defines both legacy proof and claim.proof with different values. " +
                "Use claim.proof only.");
            valid = false;
        }

        if (route.Proof != null && route.Claim.Proof == null)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval route '{route.ID}' uses legacy top-level proof. " +
                "Move it to claim.proof and set claim.mode: DestinationProof.");
        }

        var claimMode = ResolveRetrievalClaimMode(route, proof);
        switch (claimMode)
        {
            case NcRetrievalClaimMode.StoreCargo:
                if (proof != null)
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval route '{route.ID}' uses claim.mode=StoreCargo but also defines proof. " +
                        "StoreCargo routes must be completed by delivered cargo only.");
                    valid = false;
                }

                if (destination != null && destination.Target.Type == NcRetrievalDestinationTargetType.MarkerGroup)
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval route '{route.ID}' uses claim.mode=StoreCargo with MarkerGroup destination. " +
                        "Use StoreUi/ContainerGroup for store-owned delivery or DestinationProof for remote marker delivery.");
                    valid = false;
                }
                break;

            case NcRetrievalClaimMode.DestinationProof:
                if (proof == null)
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval route '{route.ID}' uses claim.mode=DestinationProof but has no claim.proof preset.");
                    valid = false;
                }

                if (source == null || !source.SpawnCargo)
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval route '{route.ID}' uses claim.mode=DestinationProof but has no spawned cargo source. " +
                        "Remote proof delivery requires source.spawnCargo: true.");
                    valid = false;
                }

                if (destination != null && destination.Target.Type == NcRetrievalDestinationTargetType.StoreUi)
                {
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval route '{route.ID}' uses claim.mode=DestinationProof with StoreUi destination. " +
                        "Use StoreCargo for direct store delivery.");
                    valid = false;
                }
                break;

            default:
                Sawmill.Warning($"[ContractsV2] Retrieval route '{route.ID}' uses unsupported claim.mode={claimMode}.");
                valid = false;
                break;
        }

        return valid;
    }

    private bool TryValidateRetrievalRouteSource(string routeId, NcRetrievalSourcePresetPrototype source)
    {
        if (!source.SpawnCargo)
            return true;

        return TryValidateRetrievalSpawnPointSelector(routeId, source.Point);
    }

    private bool TryValidateRetrievalRouteDestination(string routeId, NcRetrievalDestinationPresetPrototype destination)
    {
        switch (destination.Target.Type)
        {
            case NcRetrievalDestinationTargetType.StoreUi:
                return true;

            case NcRetrievalDestinationTargetType.MarkerGroup:
                if (!string.IsNullOrWhiteSpace(destination.Target.Id) && destination.Radius > 0)
                    return true;

                Sawmill.Warning($"[ContractsV2] Retrieval destination '{destination.ID}' for route '{routeId}' must define MarkerGroup id and radius > 0.");
                return false;

            case NcRetrievalDestinationTargetType.ContainerGroup:
                if (!string.IsNullOrWhiteSpace(destination.Target.Id))
                    return true;

                Sawmill.Warning($"[ContractsV2] Retrieval destination '{destination.ID}' for route '{routeId}' must define ContainerGroup id.");
                return false;

            default:
                Sawmill.Warning($"[ContractsV2] Retrieval destination '{destination.ID}' for route '{routeId}' uses unsupported type {destination.Target.Type}.");
                return false;
        }
    }

    private bool TryValidateRetrievalRouteProof(string routeId, NcRetrievalProofPresetPrototype proof)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(proof.Prototype) || !_prototypes.HasIndex<EntityPrototype>(proof.Prototype))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval proof preset '{proof.ID}' for route '{routeId}' references missing proof prototype '{proof.Prototype}'.");
            valid = false;
        }

        if (proof.Ownership != NcRetrievalProofOwnership.Bearer)
        {
            Sawmill.Warning($"[ContractsV2] Retrieval proof preset '{proof.ID}' uses ownership={proof.Ownership}. Stage 5.8R supports Bearer only.");
            valid = false;
        }

        if (proof.Reissue != NcRetrievalProofReissuePolicy.Never)
        {
            Sawmill.Warning($"[ContractsV2] Retrieval proof preset '{proof.ID}' uses reissue={proof.Reissue}. Stage 5.8R supports Never only.");
            valid = false;
        }

        if (!proof.ConsumeOnRewardClaim)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval proof preset '{proof.ID}' uses consumeOnRewardClaim=false. " +
                "Stage 5.8R-C always consumes bearer proof on reward claim.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateRetrievalRouteGuidance(
        string routeId,
        NcRetrievalGuidancePresetPrototype guidance,
        NcRetrievalSourcePresetPrototype? source,
        NcRetrievalProofPresetPrototype? proof)
    {
        if (!guidance.Pinpointer.Enabled)
            return true;

        var valid = true;
        if (guidance.Pinpointer.Target == NcRetrievalPinpointerTargetMode.CargoThenDestinationThenStore &&
            (source == null || !source.SpawnCargo))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval guidance '{guidance.ID}' for route '{routeId}' targets cargo, " +
                "but the route has no source.spawnCargo.");
            valid = false;
        }

        var proto = string.IsNullOrWhiteSpace(guidance.Pinpointer.Prototype)
            ? NcContractTuning.DefaultContractPinpointerPrototypeId
            : guidance.Pinpointer.Prototype;

        if (!_prototypes.HasIndex<EntityPrototype>(proto))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval guidance '{guidance.ID}' references missing pinpointer prototype '{proto}'.");
            valid = false;
        }

        return valid;
    }

    private bool TryValidateRetrievalSpawn(NcRetrievalContractPrototype proto)
    {
        var spawn = proto.Spawn;
        if (spawn == null || !spawn.Enabled)
            return true;

        var valid = true;

        if (spawn.Point == null)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has spawn enabled but no spawn.point selector.");
            valid = false;
        }
        else if (!TryValidateRetrievalSpawnPointSelector(proto.ID, spawn.Point))
        {
            valid = false;
        }

        for (var i = 0; i < proto.Targets.Count; i++)
        {
            var target = proto.Targets[i];
            if (string.IsNullOrWhiteSpace(target.Group))
                continue;

            if (!_prototypes.TryIndex<NcItemGroupPrototype>(target.Group, out var group))
                continue;

            if (TryValidateRetrievalSpawnableGroup(proto.ID, i, target.Group, group))
                continue;

            valid = false;
        }

        return valid;
    }

    private bool TryValidateRetrievalSpawnPointSelector(
        string contractId,
        ContractPointSelectorPrototype selector)
    {
        return selector.Type switch
        {
            ContractPointSelectorType.MarkerId => RequireRetrievalSpawnPointId(contractId, selector),
            ContractPointSelectorType.MarkerGroup => RequireRetrievalSpawnPointId(contractId, selector),
            ContractPointSelectorType.Weighted => TryValidateRetrievalSpawnWeightedSelector(contractId, selector),
            ContractPointSelectorType.Store => RejectRetrievalStoreSpawnPoint(contractId),
            _ => RejectRetrievalUnknownSpawnPoint(contractId, selector.Type)
        };
    }

    private static bool RequireRetrievalSpawnPointId(string contractId, ContractPointSelectorPrototype selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.Id))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point uses {selector.Type} but has no id.");
        return false;
    }

    private bool TryValidateRetrievalSpawnWeightedSelector(
        string contractId,
        ContractPointSelectorPrototype selector)
    {
        if (selector.Options.Count == 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' spawn.point is Weighted but has no options.");
            return false;
        }

        var valid = true;
        var usable = 0;
        for (var i = 0; i < selector.Options.Count; i++)
        {
            var option = selector.Options[i];
            if (option.Weight <= 0)
            {
                Sawmill.Warning(
                    $"[ContractsV2] Retrieval contract '{contractId}' spawn.point option #{i} has non-positive weight={option.Weight}.");
                valid = false;
                continue;
            }

            switch (option.Type)
            {
                case ContractPointSelectorType.MarkerId:
                case ContractPointSelectorType.MarkerGroup:
                    if (string.IsNullOrWhiteSpace(option.Id))
                    {
                        Sawmill.Warning(
                            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point option #{i} uses {option.Type} but has no id.");
                        valid = false;
                        continue;
                    }

                    usable++;
                    break;

                default:
                    Sawmill.Warning(
                        $"[ContractsV2] Retrieval contract '{contractId}' spawn.point option #{i} uses unsupported type {option.Type}. " +
                        "Retrieval spawn points must use MarkerId or MarkerGroup.");
                    valid = false;
                    break;
            }
        }

        return valid && usable > 0;
    }

    private static bool RejectRetrievalStoreSpawnPoint(string contractId)
    {
        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point cannot be Store. " +
            "Use MarkerId, MarkerGroup, or Weighted marker options.");
        return false;
    }

    private static bool RejectRetrievalUnknownSpawnPoint(string contractId, ContractPointSelectorType type)
    {
        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' spawn.point has unsupported selector type {type}.");
        return false;
    }

    private bool TryValidateRetrievalSpawnableGroup(
        string contractId,
        int index,
        string groupId,
        NcItemGroupPrototype group)
    {
        for (var i = 0; i < group.Prototypes.Count; i++)
        {
            var prototypeId = group.Prototypes[i];
            if (string.IsNullOrWhiteSpace(prototypeId))
                continue;

            if (_prototypes.HasIndex<EntityPrototype>(prototypeId))
                return true;
        }

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' has spawn enabled but target #{index} uses group '{groupId}' " +
            "with no spawnable entity prototypes. Tags-only groups can match turn-in items, but cannot spawn retrieval items.");
        return false;
    }

    private bool TryValidateRetrievalTargetCount(NcRetrievalContractPrototype proto)
    {
        if (!IsSupplyTargetCountConfigured(proto.TargetCount))
            return true;

        var range = proto.TargetCount;
        if (range.Min < 1 || range.Max < 1 || range.Min > range.Max)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has invalid targetCount range " +
                $"{range.Min}..{range.Max}. Expected min >= 1, max >= min.");
            return false;
        }

        if (proto.Targets.Count > 0 && range.Max > proto.Targets.Count)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{proto.ID}' has targetCount max={range.Max}, " +
                $"but only {proto.Targets.Count} targets are defined.");
            return false;
        }

        return true;
    }

    private bool TryValidateRetrievalCargo(
        string contractId,
        int index,
        NcSupplyTargetEntry entry)
    {
        var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
        var hasGroup = !string.IsNullOrWhiteSpace(entry.Group);

        if (hasPrototype == hasGroup)
        {
            Sawmill.Warning(
                hasPrototype
                    ? $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} has both prototype and group. Use exactly one."
                    : $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} has neither prototype nor group.");
            return false;
        }

        if (!IsCountConfigured(entry.Count))
        {
            Sawmill.Warning($"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} does not define 'count'.");
            return false;
        }

        if (!IsStrictPositiveRange(entry.Count))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} has invalid count range " +
                $"{entry.Count.Min}..{entry.Count.Max}. Expected min > 0, max > 0, min <= max.");
            return false;
        }

        if (entry.Weight <= 0)
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} has non-positive weight={entry.Weight}. " +
                "Weight is used when targetCount is configured and must be > 0.");
            return false;
        }

        if (hasPrototype)
        {
            if (_prototypes.HasIndex<EntityPrototype>(entry.Prototype))
                return true;

            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} references missing entity prototype " +
                $"'{entry.Prototype}'.");
            return false;
        }

        if (!_prototypes.TryIndex<NcItemGroupPrototype>(entry.Group, out var group))
        {
            Sawmill.Warning(
                $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} references missing ncItemGroup " +
                $"'{entry.Group}'. Retrieval V2 cargo groups must reference ncItemGroup prototypes, not legacy matchers.");
            return false;
        }

        if (!TryValidateItemGroup(contractId, entry.Group, group))
            return false;

        if (TryGetContractMatcherSpec(entry.Group, out _))
            return true;

        Sawmill.Warning(
            $"[ContractsV2] Retrieval contract '{contractId}' cargo #{index} references invalid item group '{entry.Group}'.");
        return false;
    }
}
