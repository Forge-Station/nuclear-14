using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private readonly Dictionary<ContractExecutionKind, IContractTargetResolver> _targetResolvers = new();

    private void InitializeTargetResolvers()
    {
        _targetResolvers.Clear();
        RegisterTargetResolver(new InventoryDeliveryTargetResolver());
        RegisterTargetResolver(new TrackedDeliveryTargetResolver());
        RegisterTargetResolver(new RetrievalRouteDeliveryTargetResolver());
        RegisterTargetResolver(new HuntTargetResolver());
        RegisterTargetResolver(new GhostRoleTargetResolver());
        RegisterAdditionalTargetResolvers();
    }

    // Downstream projects can implement this in another NcContractSystem partial file
    // and register target resolvers for custom objective routing/pinpointer behavior.
    partial void RegisterAdditionalTargetResolvers();

    private void RegisterTargetResolver(IContractTargetResolver resolver)
    {
        if (_targetResolvers.ContainsKey(resolver.Kind))
        {
            Sawmill.Warning($"[Contracts] Duplicate target resolver for {resolver.Kind}; replacing previous resolver.");
        }

        _targetResolvers[resolver.Kind] = resolver;
    }

    private bool TryGetTargetResolver(ContractExecutionKind kind, out IContractTargetResolver resolver)
    {
        return _targetResolvers.TryGetValue(kind, out resolver!);
    }

    private interface IContractTargetResolver
    {
        ContractExecutionKind Kind { get; }

        bool TryRefreshPinpointerState(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract);

        bool TryResolvePinpointerTarget(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target);

        bool TryResolvePinpointerTarget(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target);
    }

    private abstract class ContractTargetResolverBase : IContractTargetResolver
    {
        public abstract ContractExecutionKind Kind { get; }

        public virtual bool TryRefreshPinpointerState(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract)
        {
            return system.TryUpdateRetrievalRouteDeliveryProgress(store, contractId, contract);
        }

        public virtual bool TryResolvePinpointerTarget(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target)
        {
            target = EntityUid.Invalid;

            if (system.TryResolveRetrievalRouteReturnPinpointerTargetForUser(store, user, contract, state, out target))
                return true;

            if (UsesRetrievalSpawnedPinpointerTarget(contract))
                return system.TryResolveRetrievalSpawnedPinpointerTargetForUser(store, user, contract, state, out target);

            if (TryResolveCompletedProofTarget(system, contract, state, out target))
                return true;

            return TryResolveActiveObjectiveTargetForUser(system, store, user, contract, state, out target);
        }

        public virtual bool TryResolvePinpointerTarget(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target)
        {
            target = EntityUid.Invalid;

            if (system.TryResolveRetrievalRouteReturnPinpointerTarget(store, contract, state, out target))
                return true;

            if (UsesRetrievalSpawnedPinpointerTarget(contract) &&
                system.TryResolveRetrievalSpawnedPinpointerTarget(store, contract, state, out target))
            {
                return true;
            }

            if (TryResolveCompletedProofTarget(system, contract, state, out target))
                return true;

            return TryResolveActiveObjectiveTarget(system, store, contract, state, out target);
        }

        protected virtual bool TryResolveActiveObjectiveTarget(
            NcContractSystem system,
            EntityUid store,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target)
        {
            target = EntityUid.Invalid;

            if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && !state.GhostRoleTaken)
                return false;

            if (IsSpawnedHuntContract(contract))
                return system.TryResolveSpawnedHuntPinpointerTarget(store, contract, state, out target);

            if (state.TargetEntity is not { } tracked ||
                tracked == EntityUid.Invalid ||
                system.TerminatingOrDeleted(tracked))
            {
                return false;
            }

            target = ResolveObjectivePinpointerTarget(contract, state, tracked);
            return target != EntityUid.Invalid && !system.TerminatingOrDeleted(target);
        }

        protected virtual bool TryResolveActiveObjectiveTargetForUser(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target)
        {
            target = EntityUid.Invalid;

            if (contract.ExecutionKind == ContractExecutionKind.GhostRoleObjective && !state.GhostRoleTaken)
                return false;

            if (IsSpawnedHuntContract(contract))
                return system.TryResolveSpawnedHuntPinpointerTargetForUser(store, user, contract, state, out target);

            return TryResolveActiveObjectiveTarget(system, store, contract, state, out target);
        }

        protected bool TryResolveCompletedProofTarget(
            NcContractSystem system,
            ContractServerData contract,
            ObjectiveRuntimeState state,
            out EntityUid target)
        {
            target = EntityUid.Invalid;
            if (!contract.Completed)
                return false;

            if (state.ProofEntity is not { } proof ||
                proof == EntityUid.Invalid ||
                system.TerminatingOrDeleted(proof))
            {
                return false;
            }

            target = proof;
            return true;
        }
    }

    private sealed class InventoryDeliveryTargetResolver : ContractTargetResolverBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.InventoryDelivery;
    }

    private sealed class TrackedDeliveryTargetResolver : ContractTargetResolverBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.TrackedDeliveryObjective;
    }

    private sealed class RetrievalRouteDeliveryTargetResolver : ContractTargetResolverBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.RetrievalRouteDelivery;
    }

    private sealed class HuntTargetResolver : ContractTargetResolverBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.HuntObjective;
    }

    private sealed class GhostRoleTargetResolver : ContractTargetResolverBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.GhostRoleObjective;
    }
}
