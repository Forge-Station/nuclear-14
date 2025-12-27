using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-logic");

    private static readonly IComparer<string> OrdinalIds = new OrdinalIdComparer();

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    private readonly Dictionary<string, int> _inheritanceDepthCache = new();
    private readonly Dictionary<EntityUid, List<EntityUid>> _inventoryCache = new();
    private readonly Dictionary<string, string?> _productStackTypeCache = new();
    private readonly Dictionary<string, string[]> _protoAndAncestorsCache = new();

    [Dependency] private readonly IPrototypeManager _protos = default!;
    private readonly List<EntityUid> _scratchItems = new();
    private readonly List<(EntityUid Ent, int Count)> _scratchCurrencyCandidates = new();
    private readonly Queue<EntityUid> _scratchQueue = new();
    private readonly List<EntityUid> _scratchResult = new();
    private readonly HashSet<EntityUid> _scratchVisited = new();
    [Dependency] private readonly SharedStackSystem _stacks = default!;


    public override void Initialize()
    {
        base.Initialize();

        InitializeServices();

        _protos.PrototypesReloaded += OnPrototypesReloaded;
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
    }

    public override void Shutdown()
    {
        _protos.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent ev) => _inventoryCache.Remove(ev.Entity);

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        _productStackTypeCache.Clear();
        _protoAndAncestorsCache.Clear();
        _inventoryCache.Clear();
        _inheritanceDepthCache.Clear();
    }

    public void ResetFrameCache() => _inventoryCache.Clear();

    public void InvalidateInventoryCache(EntityUid root) => _inventoryCache.Remove(root);

    public EntityUid? GetPulledClosedCrate(EntityUid user) =>
        TryGetPulledClosedCrate(user, out var crate) ? crate : null;

    public bool TryGetPulledClosedCrate(EntityUid user, out EntityUid crate)
    {
        crate = default;

        if (TryComp<HandsComponent>(user, out var hands))
        {
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.HeldEntity is not { } held)
                    continue;

                if (TryComp<EntityStorageComponent>(held, out var storage) && !storage.Open)
                {
                    crate = held;
                    return true;
                }
            }
        }

        if (!TryComp(user, out PullerComponent? puller) || puller.Pulling is not { } pulled)
            return false;

        if (!TryComp<EntityStorageComponent>(pulled, out var pulledStorage) || pulledStorage.Open)
            return false;

        crate = pulled;
        return true;
    }

    private sealed class OrdinalIdComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) => string.CompareOrdinal(x, y);
    }
}
