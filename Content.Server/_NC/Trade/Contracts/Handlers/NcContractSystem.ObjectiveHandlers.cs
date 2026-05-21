using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private readonly Dictionary<ContractExecutionKind, IContractObjectiveHandler> _objectiveHandlers = new();

    private void InitializeObjectiveHandlers()
    {
        _objectiveHandlers.Clear();
        RegisterObjectiveHandler(new InventoryDeliveryObjectiveHandler());
        RegisterObjectiveHandler(new TrackedDeliveryObjectiveHandler());
        RegisterObjectiveHandler(new RetrievalRouteDeliveryObjectiveHandler());
        RegisterObjectiveHandler(new HuntObjectiveHandler());
        RegisterObjectiveHandler(new GhostRoleObjectiveHandler());
        RegisterAdditionalObjectiveHandlers();
    }

    // Downstream projects can implement this in another NcContractSystem partial file
    // and register their own objective handlers without touching the core dispatch paths.
    partial void RegisterAdditionalObjectiveHandlers();

    private void RegisterObjectiveHandler(IContractObjectiveHandler handler)
    {
        if (_objectiveHandlers.ContainsKey(handler.Kind))
            Sawmill.Warning($"[Contracts] Duplicate objective handler for {handler.Kind}; replacing previous handler.");

        _objectiveHandlers[handler.Kind] = handler;
    }

    private bool TryGetObjectiveHandler(ContractExecutionKind kind, out IContractObjectiveHandler handler) =>
        _objectiveHandlers.TryGetValue(kind, out handler!);

    private interface IContractObjectiveHandler
    {
        ContractExecutionKind Kind { get; }

        // Objective handlers own dispatch/orchestration only. Claim-time destructive
        // mutations must still go through the existing reward, inventory and objective
        // journals so rollback behavior stays centralized.
        ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        );

        bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        );

        bool TryUpdateProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            IReadOnlyList<EntityUid> userItems,
            IReadOnlyList<EntityUid>? crateItems
        );

        void RefreshObjectiveProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract
        );

        void AnalyzeProgressRequirements(
            ContractServerData contract,
            ref bool needsUserItems,
            ref bool needsCrateItems,
            ref bool needsStoreWorldItems
        );

        void OnTrackedTargetResolved(
            NcContractSystem system,
            (EntityUid Store, string ContractId) key,
            NcStoreComponent comp,
            ContractServerData contract
        );
    }

    private abstract class ContractObjectiveHandlerBase : IContractObjectiveHandler
    {
        public abstract ContractExecutionKind Kind { get; }

        public abstract ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        );

        public virtual bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        ) =>
            true;

        public virtual bool TryUpdateProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            IReadOnlyList<EntityUid> userItems,
            IReadOnlyList<EntityUid>? crateItems
        ) =>
            false;

        public virtual void RefreshObjectiveProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract
        )
        {
            SyncObjectiveProgressFromRuntime(contract);
            ResetContractTargetProgress(contract);
            SyncContractFlowStatus(contract);
        }

        public virtual void AnalyzeProgressRequirements(
            ContractServerData contract,
            ref bool needsUserItems,
            ref bool needsCrateItems,
            ref bool needsStoreWorldItems
        ) { }

        public virtual void OnTrackedTargetResolved(
            NcContractSystem system,
            (EntityUid Store, string ContractId) key,
            NcStoreComponent comp,
            ContractServerData contract
        ) { }
    }

    private sealed class InventoryDeliveryObjectiveHandler : ContractObjectiveHandlerBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.InventoryDelivery;

        public override ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.TryClaimInventoryDeliveryContract(store, user, contractId, comp, contract);

        public override bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        ) =>
            system.TryInitializeInventoryDeliverySupportRuntime(store, user, contractId, contract);

        public override void AnalyzeProgressRequirements(
            ContractServerData contract,
            ref bool needsUserItems,
            ref bool needsCrateItems,
            ref bool needsStoreWorldItems
        )
        {
            needsUserItems = true;
            needsCrateItems = true;
            needsStoreWorldItems |= contract.AllowsStoreWorldTurnIn;
        }
    }

    private sealed class TrackedDeliveryObjectiveHandler : ContractObjectiveHandlerBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.TrackedDeliveryObjective;

        public override ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.TryClaimTrackedDeliveryContract(store, user, contractId, comp, contract);

        public override bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        ) =>
            system.TryInitializeDeliveryObjectiveRuntime(store, user, contractId, contract);

        public override bool TryUpdateProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            IReadOnlyList<EntityUid> userItems,
            IReadOnlyList<EntityUid>? crateItems
        )
        {
            system.UpdateTrackedDeliveryObjectiveProgress(store, contractId, contract, userItems, crateItems);
            return true;
        }

        public override void AnalyzeProgressRequirements(
            ContractServerData contract,
            ref bool needsUserItems,
            ref bool needsCrateItems,
            ref bool needsStoreWorldItems
        )
        {
            needsUserItems = true;
            needsCrateItems = true;
        }

        public override void OnTrackedTargetResolved(
            NcContractSystem system,
            (EntityUid Store, string ContractId) key,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.HandleTrackedDeliveryTargetResolved(key, comp, contract);
    }

    private sealed class RetrievalRouteDeliveryObjectiveHandler : ContractObjectiveHandlerBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.RetrievalRouteDelivery;

        public override ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.TryClaimRetrievalRouteReward(store, user, contractId, comp, contract);

        public override bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        ) =>
            system.TryInitializeInventoryDeliverySupportRuntime(store, user, contractId, contract);

        public override bool TryUpdateProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            IReadOnlyList<EntityUid> userItems,
            IReadOnlyList<EntityUid>? crateItems
        )
        {
            system.TryUpdateRetrievalRouteDeliveryProgress(store, contractId, contract);
            return true;
        }
    }

    private sealed class HuntObjectiveHandler : ContractObjectiveHandlerBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.HuntObjective;

        public override ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.TryClaimObjectiveContract(store, user, contractId, comp, contract);

        public override bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        ) =>
            system.TryInitializeHuntObjectiveRuntimeOnTake(store, user, contractId, contract);

        public override bool TryUpdateProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            IReadOnlyList<EntityUid> userItems,
            IReadOnlyList<EntityUid>? crateItems
        )
        {
            system.UpdateObjectiveContractProgress(store, contractId, contract);
            return true;
        }

        public override void RefreshObjectiveProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract
        )
        {
            system.SyncHuntObjectiveProgress(store, contractId, contract);
            SyncObjectiveProgressFromRuntime(contract);
            if (IsSpawnedHuntContract(contract))
                return;

            base.RefreshObjectiveProgress(system, store, contractId, contract);
        }

        public override void OnTrackedTargetResolved(
            NcContractSystem system,
            (EntityUid Store, string ContractId) key,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.HandleHuntObjectiveTargetResolved(key, comp, contract);
    }

    private sealed class GhostRoleObjectiveHandler : ContractObjectiveHandlerBase
    {
        public override ContractExecutionKind Kind => ContractExecutionKind.GhostRoleObjective;

        public override ClaimAttemptResult TryClaim(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.TryClaimObjectiveContract(store, user, contractId, comp, contract);

        public override bool TryInitializeRuntimeOnTake(
            NcContractSystem system,
            EntityUid store,
            EntityUid user,
            string contractId,
            ContractServerData contract
        ) =>
            system.TryInitializeGhostRoleObjective(store, user, contractId, contract);

        public override bool TryUpdateProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract,
            IReadOnlyList<EntityUid> userItems,
            IReadOnlyList<EntityUid>? crateItems
        )
        {
            system.UpdateObjectiveContractProgress(store, contractId, contract);
            return true;
        }

        public override void RefreshObjectiveProgress(
            NcContractSystem system,
            EntityUid store,
            string contractId,
            ContractServerData contract
        )
        {
            system.SyncGhostRoleObjectiveProgress(store, contractId, contract);
            base.RefreshObjectiveProgress(system, store, contractId, contract);
        }

        public override void OnTrackedTargetResolved(
            NcContractSystem system,
            (EntityUid Store, string ContractId) key,
            NcStoreComponent comp,
            ContractServerData contract
        ) =>
            system.HandleGhostRoleTargetResolved(key, comp, contract);
    }
}
