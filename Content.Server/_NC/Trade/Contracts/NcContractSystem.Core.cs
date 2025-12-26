using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private const double Golden = 0.6180339887498948;
    private const double DefaultJitter = 0.06;
    private const int MaxRewardDepth = 6;
    private static readonly ISawmill Sawmill = Logger.GetSawmill("nccontracts");
    private readonly Dictionary<string, List<string>> _ancestorsCache = new();
    private readonly Dictionary<string, int> _depthCache = new();


    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<QuasiKey, double> _quasiPhase = new();
    [Dependency] private readonly IRobustRandom _random = default!;
    private readonly List<EntityUid> _scratchCrateItems = new();
    private readonly NcStoreLogicSystem.InventorySnapshot _scratchCrateSnap = new();
    private readonly List<EntityUid> _scratchUserItems = new();
    private readonly NcStoreLogicSystem.InventorySnapshot _scratchUserSnap = new();

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        _ancestorsCache.Clear();
        _depthCache.Clear();
        _quasiPhase.Clear();
    }

    public void InitContractsForStore(EntityUid uid, NcStoreComponent comp)
    {
        if (comp.Contracts.Count > 0)
            return;

        RefreshContractsInternal(uid, comp);
    }

    private static List<ContractTargetServerData> GetEffectiveTargets(ContractServerData contract) => contract.Targets;
}
