using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;


namespace Content.Server._NC.Trade;


public sealed partial class NcStoreLogicSystem : EntitySystem, IStoreRewardExecutionService, IStoreCurrencyDebitService
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-logic");
    private static readonly IComparer<string> OrdinalIds = new OrdinalIdComparer();

    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly NcStoreCurrencySystem _currency = default!;
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!;
    [Dependency] private readonly IEntityManager _ents = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly NcStoreInventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _protos = default!;

    // Phase M2: used by Buy/Sell flows to random-pick a concrete prototype from matcher.Items.
    [Dependency] private readonly IRobustRandom _random = default!;

    [Dependency] private readonly SharedStackSystem _stacks = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeServices();
    }

    public void InvalidateInventoryCache(EntityUid root) => _inventory.InvalidateInventoryCache(root);

    public void QueuePickupToHandsOrCrateNextTick(EntityUid user, EntityUid spawned) =>
        Timer.Spawn(
            0,
            () =>
            {
                if (!Exists(user) || !Exists(spawned))
                    return;

                if (_ents.TryGetComponent(spawned, out TransformComponent? xform) && xform.ParentUid == user)
                {
                    InvalidateInventoryCache(user);
                    return;
                }

                var pickedUp = false;
                if (_ents.HasComponent<HandsComponent>(user))
                {
                    try
                    {
                        pickedUp = _hands.TryPickupAnyHand(user, spawned, false);
                    }
                    catch (Exception e)
                    {
                        Sawmill.Warning(
                            $"[NcStore] Failed to pick up reward entity {ToPrettyString(spawned)} for {ToPrettyString(user)}: {e}");
                    }
                }

                if (pickedUp)
                {
                    InvalidateInventoryCache(user);
                    return;
                }

                if (TryGetPulledClosedCrate(user, out var crate) && Exists(crate))
                {
                    try
                    {
                        _entityStorage.Insert(spawned, crate);
                        InvalidateInventoryCache(crate);
                    }
                    catch (Exception e)
                    {
                        Sawmill.Warning(
                            $"[NcStore] Failed to insert reward entity {ToPrettyString(spawned)} into crate {ToPrettyString(crate)}: {e}");
                    }
                }

                InvalidateInventoryCache(user);
            });

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
