using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreInventorySystem : EntitySystem
{
    private const int UncachedRevision = int.MinValue;
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-inventory");

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    private readonly Dictionary<EntityUid, InventoryCacheEntry> _inventoryCache = new();

    private readonly Dictionary<string, string?> _productStackTypeCache = new(StringComparer.Ordinal);
    [Dependency] private readonly IPrototypeManager _protos = default!;
    private readonly HashSet<EntityUid> _rebuildOldItemsScratch = new();

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _rootsByItem = new();
    private readonly Queue<EntityUid> _scratchQueue = new();
    private readonly List<EntityUid> _scratchResult = new();
    private readonly HashSet<EntityUid> _scratchVisited = new();
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    private readonly List<EntityUid> _takeTransactionDeleteScratch = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _takeTransactionStackRestoreScratch = new();
    private bool _takeTransactionActive;

    public override void Initialize()
    {
        base.Initialize();
        _protos.PrototypesReloaded += OnPrototypesReloaded;
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);
        SubscribeLocalEvent<EntParentChangedMessage>(OnEntityParentChanged);
    }

    private void OnEntityParentChanged(ref EntParentChangedMessage ev)
    {
        if (!_rootsByItem.TryGetValue(ev.Entity, out var affectedRoots) || affectedRoots.Count == 0)
            return;

        foreach (var root in affectedRoots)
            if (_inventoryCache.TryGetValue(root, out var entry))
                MarkInventoryDirty(entry, false);
    }

    public override void Shutdown()
    {
        _protos.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        _productStackTypeCache.Clear();
        _matcherService.Clear();

        InvalidateAllCaches();
    }

    private sealed class InventoryCacheEntry
    {
        public readonly List<EntityUid> Items = new();
        public readonly NcInventorySnapshot Snapshot = new();
        public int ItemsRevision = UncachedRevision;
        public int Revision;
        public int SnapshotRevision = UncachedRevision;
    }

    private readonly record struct ProductTakeRequest(
        string ProtoId,
        string? StackType,
        PrototypeMatchMode MatchMode,
        CompiledMatcher? Matcher,
        bool IsValid);
}
