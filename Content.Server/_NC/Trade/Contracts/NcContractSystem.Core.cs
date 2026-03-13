using Content.Shared._NC.Trade;
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
    private readonly Dictionary<string, List<string>> _ancestorsCache = new(StringComparer.Ordinal);
    private readonly List<(EntityUid Store, string Difficulty)> _cooldownKeysToRemoveScratch = new();
    private readonly Dictionary<(EntityUid Store, string Difficulty), CooldownState> _contractCooldown = new();
    private readonly Dictionary<string, int> _depthCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, (StoreContractPrototype Proto, int Weight)>> _flattenedPoolCache = new(StringComparer.Ordinal);
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> _progressClaimableByKeyScratch = new();
    private readonly HashSet<EntityUid> _progressConsumedEntitiesScratch = new();
    private readonly List<(string ProtoId, PrototypeMatchMode MatchMode, int Depth)> _progressOrderedKeysScratch = new();
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> _progressRequiredByKeyScratch = new();
    private readonly Stack<List<int>> _progressTargetIndexPool = new();
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), List<int>> _progressTargetIndexesByKeyScratch = new();
    private readonly Dictionary<EntityUid, int> _progressVirtualStackLeftScratch = new();
    private readonly Dictionary<QuasiKey, double> _quasiPhase = new();
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly List<string> _progressContractIdsScratch = new();
    private readonly List<EntityUid> _scratchCrateItems = new();
    private readonly List<EntityUid> _scratchUserItems = new();
    private readonly Dictionary<QuasiKey, SmallBagState> _smallBags = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeObjectiveRuntime();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        ShutdownObjectiveRuntime();
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev) => ClearCaches();

    private void ClearCaches()
    {
        _ancestorsCache.Clear();
        _depthCache.Clear();

        _quasiPhase.Clear();
        _smallBags.Clear();

        _cooldownKeysToRemoveScratch.Clear();
        _contractCooldown.Clear();
        _flattenedPoolCache.Clear();
    }

    public void ClearStoreRuntimeCaches(EntityUid store)
    {
        if (store == EntityUid.Invalid)
            return;

        if (_contractCooldown.Count > 0)
        {
            _cooldownKeysToRemoveScratch.Clear();
            foreach (var key in _contractCooldown.Keys)
            {
                if (key.Store == store)
                    _cooldownKeysToRemoveScratch.Add(key);
            }

            for (var i = 0; i < _cooldownKeysToRemoveScratch.Count; i++)
                _contractCooldown.Remove(_cooldownKeysToRemoveScratch[i]);

            _cooldownKeysToRemoveScratch.Clear();
        }

        ClearStoreObjectiveRuntime(store, deleteTrackedEntities: true);
    }

    private static List<ContractTargetServerData> GetEffectiveTargets(ContractServerData contract) => contract.Targets;

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

    private sealed class SmallBagState
    {
        public readonly List<int> Order = new();
        public int Cursor;
        public int LastIdx = -1;
        public int Max;
        public int Min;
    }

    private sealed class CooldownState
    {
        public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
        public readonly Queue<string> Queue = new();

        public int Limit;

        public bool Contains(string id) => Limit > 0 && Counts.ContainsKey(id);

        public void TrimToLimit()
        {
            if (Limit <= 0)
            {
                Queue.Clear();
                Counts.Clear();
                return;
            }

            while (Queue.Count > Limit)
            {
                var old = Queue.Dequeue();

                if (!Counts.TryGetValue(old, out var c))
                    continue;

                c--;
                if (c <= 0)
                    Counts.Remove(old);
                else
                    Counts[old] = c;
            }
        }

        public void Push(string id)
        {
            if (Limit <= 0 || string.IsNullOrWhiteSpace(id))
                return;

            Queue.Enqueue(id);

            Counts.TryGetValue(id, out var c);
            Counts[id] = c + 1;

            TrimToLimit();
        }
    }
}
