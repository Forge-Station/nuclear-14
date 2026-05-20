using Content.Shared._NC.Trade;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreInventorySystem : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-inventory");
    private const int UncachedRevision = int.MinValue;

    private sealed class InventoryCacheEntry
    {
        public readonly List<EntityUid> Items = new();
        public readonly NcInventorySnapshot Snapshot = new();
        public int Revision;
        public int ItemsRevision = UncachedRevision;
        public int SnapshotRevision = UncachedRevision;
    }

    private readonly record struct ProductTakeRequest(
        string ProtoId,
        string? StackType,
        PrototypeMatchMode MatchMode,
        CompiledMatcher? Matcher,
        bool IsValid);

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    private readonly Dictionary<EntityUid, InventoryCacheEntry> _inventoryCache = new();
    private readonly List<(EntityUid Ent, int PreviousCount)> _takeTransactionStackRestoreScratch = new();
    private readonly List<EntityUid> _takeTransactionDeleteScratch = new();
    [Dependency] private readonly TagSystem _tags = default!;
    private bool _takeTransactionActive;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _rootsByItem = new();
    private readonly HashSet<EntityUid> _rebuildOldItemsScratch = new();

    private readonly Dictionary<string, string?> _productStackTypeCache = new(StringComparer.Ordinal);
    [Dependency] private readonly IPrototypeManager _protos = default!;
    private readonly Queue<EntityUid> _scratchQueue = new();
    private readonly List<EntityUid> _scratchResult = new();
    private readonly HashSet<EntityUid> _scratchVisited = new();
    [Dependency] private readonly SharedStackSystem _stacks = default!;

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
        {
            if (_inventoryCache.TryGetValue(root, out var entry))
            {
                MarkInventoryDirty(entry, itemsStillCurrent: false);
            }
        }
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























































}
