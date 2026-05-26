using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private readonly Dictionary<ContractPoolCandidateKind, IContractDefinitionHandler>
        _definitionHandlersByCandidateKind = new();

    private readonly Dictionary<NcContractOfferType, IContractDefinitionHandler> _definitionHandlersByOfferType = new();

    private void InitializeDefinitionHandlers()
    {
        _definitionHandlersByOfferType.Clear();
        _definitionHandlersByCandidateKind.Clear();
        RegisterDefinitionHandler(new SupplyContractDefinitionHandler());
        RegisterDefinitionHandler(new RetrievalContractDefinitionHandler());
        RegisterDefinitionHandler(new HuntContractDefinitionHandler());
        RegisterDefinitionHandler(new GhostRoleContractDefinitionHandler());
        RegisterAdditionalDefinitionHandlers();
    }

    // Downstream projects can implement this in another NcContractSystem partial file
    // to add custom contract definition handlers without editing offer generation.
    partial void RegisterAdditionalDefinitionHandlers();

    private void RegisterDefinitionHandler(IContractDefinitionHandler handler)
    {
        if (_definitionHandlersByOfferType.ContainsKey(handler.OfferType))
        {
            Sawmill.Warning(
                $"[Contracts] Duplicate definition handler for offer type {handler.OfferType}; replacing previous handler.");
        }

        if (_definitionHandlersByCandidateKind.ContainsKey(handler.CandidateKind))
        {
            Sawmill.Warning(
                $"[Contracts] Duplicate definition handler for candidate kind {handler.CandidateKind}; replacing previous handler.");
        }

        _definitionHandlersByOfferType[handler.OfferType] = handler;
        _definitionHandlersByCandidateKind[handler.CandidateKind] = handler;
    }

    private bool TryGetDefinitionHandler(NcContractOfferType type, out IContractDefinitionHandler handler) =>
        _definitionHandlersByOfferType.TryGetValue(type, out handler!);

    private bool TryGetDefinitionHandler(ContractPoolCandidateKind kind, out IContractDefinitionHandler handler) =>
        _definitionHandlersByCandidateKind.TryGetValue(kind, out handler!);

    private interface IContractDefinitionHandler
    {
        NcContractOfferType OfferType { get; }
        ContractPoolCandidateKind CandidateKind { get; }

        bool TryCreateCandidate(
            NcContractSystem system,
            NcContractOfferPoolPrototype pool,
            NcContractOfferEntry entry,
            ContractPoolCandidate candidate
        );

        ContractServerData CreateContract(
            NcContractSystem system,
            EntityUid store,
            ContractPoolCandidate candidate
        );
    }

    private sealed class SupplyContractDefinitionHandler : IContractDefinitionHandler
    {
        public NcContractOfferType OfferType => NcContractOfferType.Supply;
        public ContractPoolCandidateKind CandidateKind => ContractPoolCandidateKind.Supply;

        public bool TryCreateCandidate(
            NcContractSystem system,
            NcContractOfferPoolPrototype pool,
            NcContractOfferEntry entry,
            ContractPoolCandidate candidate
        )
        {
            if (!system._prototypes.TryIndex<NcSupplyContractPrototype>(entry.Id, out var supply) ||
                !system.TryValidateSupplyContractForPool(pool.ID, supply))
                return false;

            candidate.Kind = CandidateKind;
            candidate.Id = supply.ID;
            candidate.Repeatable = supply.Repeatable;
            candidate.Supply = supply;
            return true;
        }

        public ContractServerData CreateContract(
            NcContractSystem system,
            EntityUid store,
            ContractPoolCandidate candidate
        ) =>
            candidate.Supply != null
                ? system.CreateSupplyContractData(store, candidate.Supply)
                : CreateInvalidContractData(candidate);
    }

    private sealed class RetrievalContractDefinitionHandler : IContractDefinitionHandler
    {
        public NcContractOfferType OfferType => NcContractOfferType.Retrieval;
        public ContractPoolCandidateKind CandidateKind => ContractPoolCandidateKind.Retrieval;

        public bool TryCreateCandidate(
            NcContractSystem system,
            NcContractOfferPoolPrototype pool,
            NcContractOfferEntry entry,
            ContractPoolCandidate candidate
        )
        {
            if (!system._prototypes.TryIndex<NcRetrievalContractPrototype>(entry.Id, out var retrieval) ||
                !system.TryValidateRetrievalContractForPool(pool.ID, retrieval))
                return false;

            candidate.Kind = CandidateKind;
            candidate.Id = retrieval.ID;
            candidate.Repeatable = retrieval.Repeatable;
            candidate.Retrieval = retrieval;
            return true;
        }

        public ContractServerData CreateContract(
            NcContractSystem system,
            EntityUid store,
            ContractPoolCandidate candidate
        ) =>
            candidate.Retrieval != null
                ? system.CreateRetrievalContractData(store, candidate.Retrieval)
                : CreateInvalidContractData(candidate);
    }

    private sealed class HuntContractDefinitionHandler : IContractDefinitionHandler
    {
        public NcContractOfferType OfferType => NcContractOfferType.Hunt;
        public ContractPoolCandidateKind CandidateKind => ContractPoolCandidateKind.Hunt;

        public bool TryCreateCandidate(
            NcContractSystem system,
            NcContractOfferPoolPrototype pool,
            NcContractOfferEntry entry,
            ContractPoolCandidate candidate
        )
        {
            if (!system._prototypes.TryIndex<NcHuntContractPrototype>(entry.Id, out var hunt) ||
                !system.TryValidateHuntContractForPool(pool.ID, hunt))
                return false;

            if (hunt.Completion.Mode is not (NcHuntCompletionMode.TrophyTurnIn or NcHuntCompletionMode.BodyTurnIn))
            {
                Sawmill.Warning(
                    $"[Contracts] Hunt offer '{hunt.ID}' uses completion.mode={hunt.Completion.Mode}. " +
                    "Runtime currently supports only TrophyTurnIn and BodyTurnIn; contract skipped.");
                return false;
            }

            candidate.Kind = CandidateKind;
            candidate.Id = hunt.ID;
            candidate.Repeatable = hunt.Repeatable;
            candidate.Hunt = hunt;
            return true;
        }

        public ContractServerData CreateContract(
            NcContractSystem system,
            EntityUid store,
            ContractPoolCandidate candidate
        ) =>
            candidate.Hunt != null
                ? system.CreateHuntContractData(store, candidate.Hunt)
                : CreateInvalidContractData(candidate);
    }

    private sealed class GhostRoleContractDefinitionHandler : IContractDefinitionHandler
    {
        public NcContractOfferType OfferType => NcContractOfferType.GhostRole;
        public ContractPoolCandidateKind CandidateKind => ContractPoolCandidateKind.GhostRole;

        public bool TryCreateCandidate(
            NcContractSystem system,
            NcContractOfferPoolPrototype pool,
            NcContractOfferEntry entry,
            ContractPoolCandidate candidate
        )
        {
            if (!system._prototypes.TryIndex<NcGhostRoleContractPrototype>(entry.Id, out var ghostRole) ||
                !system.TryValidateGhostRoleContractForPool(pool.ID, ghostRole))
                return false;

            candidate.Kind = CandidateKind;
            candidate.Id = ghostRole.ID;
            candidate.Repeatable = ghostRole.Repeatable;
            candidate.GhostRole = ghostRole;
            return true;
        }

        public ContractServerData CreateContract(
            NcContractSystem system,
            EntityUid store,
            ContractPoolCandidate candidate
        ) =>
            candidate.GhostRole != null
                ? system.CreateGhostRoleContractData(store, candidate.GhostRole)
                : CreateInvalidContractData(candidate);
    }
}
