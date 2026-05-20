using System.Linq;
using Content.Server.Popups;
using Content.Server.Storage.Components;
using Content.Shared._NC.Trade;
using Content.Shared.Access.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem : EntitySystem
{
    private const float AutoCloseDistance = StoreTradeLimits.StoreUiCloseDistance;
    private const float MinAccelInterval = 0.25f;
    private const float MinDynamicInterval = 0.25f;
    private const float MinManualRefreshInterval = 0.5f;
    private const int MaxVisibleListingIds = StoreTradeLimits.MaxVisibleListingIds;
    private const int WatchedRootSearchLimit = 32;
    private static readonly TimeSpan InvalidContractWarningInterval = TimeSpan.FromSeconds(5);
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-structured");
    private static readonly TimeSpan RealtimeOpenStoreUpdateInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan OpenStoreValidityCheckInterval = TimeSpan.FromSeconds(0.5);
    private readonly HashSet<EntityUid> _affectedStoresScratch = new();
    private readonly HashSet<EntityUid> _storesUpdatingDynamic = new();
    private readonly List<string> _visibleListingIdsScratch = new(MaxVisibleListingIds);
    private readonly HashSet<string> _visibleListingIdsSetScratch = new(StringComparer.Ordinal);
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly NcContractSystem _contracts = default!;
    private readonly HashSet<EntityUid> _dirtyStores = new();
    private readonly List<EntityUid> _dirtyStoresScratch = new();
    private readonly Dictionary<EntityUid, DynamicScratch> _dynamicScratchByStore = new();
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly StoreSystemStructuredLoader _loader = default!;
    [Dependency] private readonly NcStoreLogicSystem _logic = default!;
    private readonly List<EntityUid> _openStoresScratch = new();
    private readonly HashSet<EntityUid> _openStoreUids = new();
    private readonly HashSet<EntityUid> _pendingRefreshEntities = new();
    [Dependency] private readonly PopupSystem _popups = default!;
    private readonly Dictionary<string, TimeSpan> _nextInvalidContractWarningByActor = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _storesByWatchedRoot = new();
    [Dependency] private readonly NcStoreSystem _storeSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<EntityUid, (EntityUid User, EntityUid? Crate)> _watchByStore = new();

    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private TimeSpan _nextAccelAllowed = TimeSpan.Zero;
    private TimeSpan _nextRealtimeOpenStoreUpdate = TimeSpan.Zero;
    private TimeSpan _nextOpenStoreValidityCheck = TimeSpan.Zero;
    private const int MaxDynamicUpdatesPerTick = 8;

    private DynamicScratch GetDynamicScratch(EntityUid storeUid)
    {
        if (_dynamicScratchByStore.TryGetValue(storeUid, out var scratch))
            return scratch;

        scratch = new();
        _dynamicScratchByStore[storeUid] = scratch;
        return scratch;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NcStoreComponent, ActivatableUIOpenAttemptEvent>(OnUiOpenAttempt);
        SubscribeLocalEvent<NcStoreComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<NcStoreComponent, RequestUiRefreshMessage>(OnUiRefreshRequest);
        SubscribeLocalEvent<NcStoreComponent, StoreSetVisibleListingsBoundUiMessage>(OnSetVisibleListings);
        SubscribeLocalEvent<NcStoreComponent, NcContractsChangedEvent>(OnContractsChanged);
        SubscribeLocalEvent<AccessReaderComponent, AccessReaderConfigurationChangedEvent>(OnAccessReaderChanged);
        SubscribeLocalEvent<NcStoreComponent, ComponentShutdown>(OnStoreShutdown);
        SubscribeLocalEvent<ContainerManagerComponent, EntInsertedIntoContainerMessage>(OnUserEntInserted);
        SubscribeLocalEvent<ContainerManagerComponent, EntRemovedFromContainerMessage>(OnUserEntRemoved);
        SubscribeLocalEvent<StackComponent, StackCountChangedEvent>(OnStackCountChanged);
        SubscribeLocalEvent<EntParentChangedMessage>(OnWatchedEntityParentChanged);
        SubscribeLocalEvent<NcStoreComponent, ClaimContractBoundMessage>(OnClaimContract);
        SubscribeLocalEvent<NcStoreComponent, TakeContractBoundMessage>(OnTakeContract);
        SubscribeLocalEvent<NcStoreComponent, SkipContractBoundMessage>(OnSkipContract);
        SubscribeLocalEvent<NcStoreComponent, RequestContractPinpointerBoundMessage>(OnRequestContractPinpointer);
        SubscribeLocalEvent<EntityStorageComponent, StorageAfterOpenEvent>(OnStorageOpen);
        SubscribeLocalEvent<EntityStorageComponent, StorageAfterCloseEvent>(OnStorageClose);
    }















































}
