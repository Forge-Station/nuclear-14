using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


/// <summary>
///     Stack-based currency implementation.
///     Currency id is interpreted as <see cref="StackComponent.StackTypeId" /> / <see cref="StackPrototype" /> id.
/// </summary>
public sealed partial class StackCurrencyHandler : ICurrencyHandler
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-logic");
    private readonly IEntityManager _ents;
    private readonly SharedHandsSystem _hands;
    private readonly NcStoreInventorySystem _inventory;
    private readonly List<EntityUid> _issueSpawnedScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _issueStackRestoreScratch = new();
    private readonly IPrototypeManager _protos;
    private readonly List<(EntityUid Ent, int Count)> _scratchCandidates = new();
    private readonly List<EntityUid> _scratchItems = new();
    private readonly SharedStackSystem _stacks;
    private readonly List<EntityUid> _takePendingDeletesScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _takeStackRestoreScratch = new();
    private readonly List<EntityUid> _transactionIssueSpawnedScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _transactionIssueStackRestoreScratch = new();
    private readonly SharedTransformSystem _xform;
    private bool _currencyIssueTransactionActive;

    public StackCurrencyHandler(
        IEntityManager ents,
        SharedHandsSystem hands,
        NcStoreInventorySystem inventory,
        IPrototypeManager protos,
        SharedStackSystem stacks,
        SharedTransformSystem xform
    )
    {
        _ents = ents;
        _hands = hands;
        _inventory = inventory;
        _protos = protos;
        _stacks = stacks;
        _xform = xform;
    }

    public bool CanHandle(string currencyId)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
            return false;

        // StackType ids are stack prototype ids. Payout also needs a valid spawn prototype,
        // otherwise sell/claim validation could accept currency that cannot actually be issued.
        return _protos.TryIndex<StackPrototype>(currencyId, out var proto) &&
            !string.IsNullOrWhiteSpace(proto.Spawn) &&
            _protos.HasIndex<EntityPrototype>(proto.Spawn);
    }

    public bool TryGetBalance(in NcInventorySnapshot snapshot, string currencyId, out int balance)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
        {
            balance = 0;
            return false;
        }

        balance = snapshot.StackTypeCounts.TryGetValue(currencyId, out var b) ? b : 0;
        return true;
    }

    public bool CanGiveCurrency(EntityUid user, string currencyId, int amount)
    {
        if (amount <= 0)
            return true;

        if (!CanHandle(currencyId))
            return false;

        return _ents.EntityExists(user) &&
            _ents.TryGetComponent(user, out TransformComponent? _);
    }
}
