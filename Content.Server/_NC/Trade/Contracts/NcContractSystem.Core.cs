using Content.Shared._NC.Trade;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private const double Golden = 0.6180339887498948;
    private const double DefaultJitter = 0.06;
    private const int MaxRewardDepth = 6;
    private const int DepthInProgress = -1;
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");
    private readonly Dictionary<string, int> _depthCache = new(StringComparer.Ordinal);
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    private readonly HashSet<(EntityUid Store, EntityUid User, string ContractId)> _claimInProgress = new();
    private bool _claimScratchInUse;
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> _progressClaimableByKeyScratch = new();
    private readonly HashSet<EntityUid> _progressConsumedEntitiesScratch = new();
    private readonly HashSet<EntityUid> _storesUpdatingProgress = new();
    private bool _progressScratchInUse;
    private readonly List<(string ProtoId, PrototypeMatchMode MatchMode, int Depth)> _progressOrderedKeysScratch = new();
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> _progressRequiredByKeyScratch = new();
    private readonly Stack<List<int>> _progressTargetIndexPool = new();
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), List<int>> _progressTargetIndexesByKeyScratch = new();
    private readonly Dictionary<EntityUid, int> _progressVirtualStackLeftScratch = new();
    private readonly Dictionary<QuasiKey, double> _quasiPhase = new();
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly List<string> _progressContractIdsScratch = new();
    private readonly List<EntityUid> _scratchCrateItems = new();
    private readonly List<EntityUid> _scratchStoreNearbyItems = new();
    private readonly List<EntityUid> _scratchUserItems = new();
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeObjectiveRuntime();
        InitializeTurnInContainerIndex();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        ShutdownTurnInContainerIndex();
        ShutdownObjectiveRuntime();
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev) => ClearCaches();

    private void ClearCaches()
    {
        _depthCache.Clear();
        _contractMatcherCache.Clear();
        ClearRngCachesInternal();

        _claimInProgress.Clear();
        _storesUpdatingProgress.Clear();
        _claimScratchInUse = false;
        _progressScratchInUse = false;
    }

    public void ClearStoreRuntimeCaches(EntityUid store)
    {
        if (store == EntityUid.Invalid)
            return;

        ClearStoreObjectiveRuntime(store, deleteTrackedEntities: true);
    }

    private static List<ContractTargetServerData> GetEffectiveTargets(ContractServerData contract)
    {
        contract.Targets ??= new();
        for (var i = contract.Targets.Count - 1; i >= 0; i--)
        {
            if (contract.Targets[i] == null)
                contract.Targets.RemoveAt(i);
        }

        return contract.Targets;
    }

    private int GetProtoDepth(string protoId)
    {
        if (_depthCache.TryGetValue(protoId, out var cached))
            return cached >= 0 ? cached : 0;

        if (!_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
        {
            _depthCache[protoId] = 0;
            return 0;
        }

        _depthCache[protoId] = DepthInProgress;

        var best = 0;
        var parents = proto.Parents;

        if (parents is { Length: > 0, })
        {
            foreach (var parentId in parents)
            {
                var depth = GetProtoDepth(parentId) + 1;
                if (depth > best)
                    best = depth;
            }
        }

        _depthCache[protoId] = best;
        return best;
    }

    private sealed class SoftFairState
    {
        public readonly List<double> Heat = new();
        public int LastIdx = -1;
        public int Max;
        public int Min;
        public int Streak;
    }

}
