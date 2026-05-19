using Content.Server._NC.Trade;
using Content.Shared;
using Content.Shared._NC.Trade;
using Content.Shared.CCVar;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Server;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._NC.Trade;

[TestFixture]
public sealed class NcStoreTransactionRegressionTest
{
    private const string TestCurrency = "NcTradeTestCredit";
    private const string TestCurrencyStack = "NcTradeTestCreditStack";
    private const string TestSaleItem = "NcTradeTestSaleItem";
    private const string TestStackedSaleItem = "NcTradeTestStackedSaleItem";
    private const string TestAbstractProduct = "NcTradeTestAbstractProduct";
    private const string TestStaticTaggedItem = "NcTradeTestStaticTaggedItem";
    private const string TestRuntimeTaggedItem = "NcTradeTestRuntimeTaggedItem";
    private const string TestMatcher = "NcTradeTestMatcher";

    private static readonly (string Cvar, string Value)[] TestCvars =
    {
        (CCVars.DatabaseSynchronous.Name, "true"),
        (CCVars.DatabaseSqliteDelay.Name, "0"),
        (CCVars.HolidaysEnabled.Name, "false"),
        (CCVars.AdminLogsQueueSendDelay.Name, "0"),
        (CVars.NetPVS.Name, "false"),
        (CCVars.NPCMaxUpdates.Name, "999999"),
        (CVars.ThreadParallelCount.Name, "1"),
        (CCVars.GameRoleTimers.Name, "false"),
        (CCVars.GameRoleWhitelist.Name, "false"),
        (CCVars.GridFill.Name, "false"),
        (CCVars.PreloadGrids.Name, "false"),
        (CCVars.ArrivalsShuttles.Name, "false"),
        (CCVars.EmergencyShuttleEnabled.Name, "false"),
        (CCVars.ProcgenPreload.Name, "false"),
        (CCVars.WorldgenEnabled.Name, "false"),
        (CVars.ReplayClientRecordingEnabled.Name, "false"),
        (CVars.ReplayServerRecordingEnabled.Name, "false"),
        (CCVars.GameDummyTicker.Name, "true"),
        (CCVars.GameLobbyEnabled.Name, "false"),
        (CCVars.ConfigPresetDevelopment.Name, "false"),
        (CCVars.AdminLogsEnabled.Name, "false"),
        (CCVars.AutosaveEnabled.Name, "false"),
        (CVars.NetBufferSize.Name, "0"),
        (CCVars.InteractionRateLimitCount.Name, "9999999"),
        (CCVars.InteractionRateLimitPeriod.Name, "0.1"),
        (CCVars.MovementMobPushing.Name, "false"),
    };

    private const string Prototypes = @"
- type: Tag
  id: NcTradeTestStaticTag

- type: Tag
  id: NcTradeTestRuntimeTag

- type: entity
  id: N14ClothingOuterNCRPouchedVestDesert

- type: entity
  parent: BaseItem
  id: NcTradeTestAbstractCurrencySpawn
  abstract: true
  components:
  - type: Stack
    stackType: NcTradeTestCredit
    count: 1

- type: stack
  id: NcTradeTestCredit
  name: test credit
  spawn: NcTradeTestAbstractCurrencySpawn
  maxCount: 10

- type: entity
  parent: BaseItem
  id: NcTradeTestCreditStack
  components:
  - type: Stack
    stackType: NcTradeTestCredit
    count: 1

- type: entity
  parent: BaseItem
  id: NcTradeTestSaleItem

- type: stack
  id: NcTradeTestSaleStack
  name: test sale stack
  spawn: NcTradeTestStackedSaleItem
  maxCount: 10

- type: entity
  parent: BaseItem
  id: NcTradeTestStackedSaleItem
  components:
  - type: Stack
    stackType: NcTradeTestSaleStack
    count: 1

- type: entity
  parent: BaseItem
  id: NcTradeTestAbstractProduct
  abstract: true

- type: entity
  parent: BaseItem
  id: NcTradeTestStaticTaggedItem
  components:
  - type: Tag
    tags:
    - NcTradeTestStaticTag

- type: entity
  parent: BaseItem
  id: NcTradeTestRuntimeTaggedItem

- type: ncMatcher
  id: NcTradeTestMatcher
  name: test matcher
  tags:
  - NcTradeTestStaticTag
  - NcTradeTestRuntimeTag
";

    [Test]
    public async Task CurrencyIssueSpawnFailureRollsBackPartialStackFill()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var stack = entMan.System<SharedStackSystem>();
        var logic = entMan.System<NcStoreLogicSystem>();

        EntityUid user = default;
        EntityUid currency = default;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            currency = entMan.SpawnEntity(TestCurrencyStack, MapCoordinates.Nullspace);
            stack.SetCount(currency, 9);

            Assert.That(hands.TryPickupAnyHand(user, currency), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(logic.TryGiveCurrency(user, TestCurrency, 2), Is.False);
            Assert.That(entMan.GetComponent<StackComponent>(currency).Count, Is.EqualTo(9));
        });

    }

    [Test]
    public async Task BuySpawnFailureDoesNotTakeCurrencyOrRemainingCount()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var stack = entMan.System<SharedStackSystem>();
        var logic = entMan.System<NcStoreLogicSystem>();

        EntityUid user = default;
        EntityUid machine = default;
        EntityUid currency = default;
        NcStoreComponent store = default!;
        NcStoreListingDef listing = default!;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            machine = user;
            store = new NcStoreComponent();
            store.CurrencyWhitelist.Add(TestCurrency);

            currency = entMan.SpawnEntity(TestCurrencyStack, MapCoordinates.Nullspace);
            stack.SetCount(currency, 5);
            Assert.That(hands.TryPickupAnyHand(user, currency), Is.True);

            listing = new NcStoreListingDef
            {
                Id = "abstract-buy",
                Mode = StoreMode.Buy,
                ProductEntity = TestAbstractProduct,
                RemainingCount = 1,
                Cost = { [TestCurrency] = 2 },
            };
            store.Listings.Add(listing);
            store.RebuildListingIndex();
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(logic.TryBuy("abstract-buy", machine, store, user), Is.False);
            Assert.That(entMan.GetComponent<StackComponent>(currency).Count, Is.EqualTo(5));
            Assert.That(listing.RemainingCount, Is.EqualTo(1));
        });

    }

    [Test]
    public async Task SellPayoutFailureDoesNotConsumeSoldItemOrRemainingCount()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var stack = entMan.System<SharedStackSystem>();
        var logic = entMan.System<NcStoreLogicSystem>();

        EntityUid user = default;
        EntityUid machine = default;
        EntityUid currency = default;
        EntityUid soldItem = default;
        NcStoreComponent store = default!;
        NcStoreListingDef listing = default!;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            machine = user;
            store = new NcStoreComponent();
            store.CurrencyWhitelist.Add(TestCurrency);

            currency = entMan.SpawnEntity(TestCurrencyStack, MapCoordinates.Nullspace);
            stack.SetCount(currency, 9);
            Assert.That(hands.TryPickupAnyHand(user, currency), Is.True);

            soldItem = entMan.SpawnEntity(TestSaleItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(user, soldItem), Is.True);

            listing = CreateSellListing("sell-fail", TestSaleItem, 2, remaining: 1);
            store.Listings.Add(listing);
            store.RebuildListingIndex();
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(logic.TrySell("sell-fail", machine, store, user), Is.False);
            Assert.That(entMan.EntityExists(soldItem), Is.True);
            Assert.That(entMan.GetComponent<StackComponent>(currency).Count, Is.EqualTo(9));
            Assert.That(listing.RemainingCount, Is.EqualTo(1));
        });

    }

    [Test]
    public async Task MassSellPayoutFailureDoesNotMutateContainerOrRemainingCount()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var stack = entMan.System<SharedStackSystem>();
        var logic = entMan.System<NcStoreLogicSystem>();

        EntityUid user = default;
        EntityUid container = default;
        EntityUid machine = default;
        EntityUid currency = default;
        EntityUid soldItem = default;
        NcStoreComponent store = default!;
        NcStoreListingDef listing = default!;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            container = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            machine = user;
            store = new NcStoreComponent();
            store.CurrencyWhitelist.Add(TestCurrency);

            currency = entMan.SpawnEntity(TestCurrencyStack, MapCoordinates.Nullspace);
            stack.SetCount(currency, 9);
            Assert.That(hands.TryPickupAnyHand(user, currency), Is.True);

            soldItem = entMan.SpawnEntity(TestSaleItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(container, soldItem), Is.True);

            listing = CreateSellListing("mass-sell-fail", TestSaleItem, 2, remaining: 1);
            store.Listings.Add(listing);
            store.RebuildListingIndex();
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(logic.TryMassSellFromContainer(machine, store, user, container), Is.False);
            Assert.That(entMan.EntityExists(soldItem), Is.True);
            Assert.That(entMan.GetComponent<StackComponent>(currency).Count, Is.EqualTo(9));
            Assert.That(listing.RemainingCount, Is.EqualTo(1));
        });

    }

    [Test]
    public async Task InventoryTakeTransactionRollbackRestoresStacksAndDeferredDeletes()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var stack = entMan.System<SharedStackSystem>();
        var inventory = entMan.System<NcStoreInventorySystem>();

        EntityUid user = default;
        EntityUid stackItem = default;
        EntityUid singleItem = default;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);

            stackItem = entMan.SpawnEntity(TestStackedSaleItem, MapCoordinates.Nullspace);
            stack.SetCount(stackItem, 5);
            Assert.That(hands.TryPickupAnyHand(user, stackItem), Is.True);

            singleItem = entMan.SpawnEntity(TestSaleItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(user, singleItem), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(inventory.BeginTakeTransaction(), Is.True);
            Assert.That(inventory.TryTakeProductUnitsFromRootCached(user, TestStackedSaleItem, 3, PrototypeMatchMode.Exact), Is.True);
            Assert.That(entMan.GetComponent<StackComponent>(stackItem).Count, Is.EqualTo(2));
            inventory.RollbackTakeTransaction();

            Assert.That(entMan.GetComponent<StackComponent>(stackItem).Count, Is.EqualTo(5));

            Assert.That(inventory.BeginTakeTransaction(), Is.True);
            Assert.That(inventory.TryTakeProductUnitsFromRootCached(user, TestSaleItem, 1, PrototypeMatchMode.Exact), Is.True);
            Assert.That(entMan.EntityExists(singleItem), Is.True);
            inventory.RollbackTakeTransaction();

            Assert.That(entMan.EntityExists(singleItem), Is.True);
            Assert.That(inventory.GetOwnedFromRootCached(user, TestSaleItem, PrototypeMatchMode.Exact), Is.EqualTo(1));
        });

    }

    [Test]
    public async Task MatcherTagsUsePrototypeTagsNotRuntimeTags()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var tags = entMan.System<TagSystem>();
        var inventory = entMan.System<NcStoreInventorySystem>();

        EntityUid user = default;
        EntityUid staticTagged = default;
        EntityUid runtimeTagged = default;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);

            staticTagged = entMan.SpawnEntity(TestStaticTaggedItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(user, staticTagged), Is.True);

            runtimeTagged = entMan.SpawnEntity(TestRuntimeTaggedItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(user, runtimeTagged), Is.True);
            Assert.That(tags.AddTag(runtimeTagged, "NcTradeTestRuntimeTag"), Is.True);
            Assert.That(tags.HasTag(runtimeTagged, "NcTradeTestRuntimeTag"), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var snapshot = inventory.BuildInventorySnapshot(user);
            Assert.That(inventory.GetOwnedFromSnapshot(snapshot, TestMatcher, PrototypeMatchMode.Matcher), Is.EqualTo(1));
            Assert.That(inventory.GetOwnedFromRootCached(user, TestMatcher, PrototypeMatchMode.Matcher), Is.EqualTo(1));
            Assert.That(inventory.PrototypeMatchesMatcher(TestMatcher, TestRuntimeTaggedItem), Is.False);
        });

    }

    private static async Task<RobustIntegrationTest.ServerIntegrationInstance> StartServer()
    {
        var logHandler = new PoolTestLogHandler("SERVER");
        logHandler.ActivateContext(TestContext.Out);

        var options = new RobustIntegrationTest.ServerIntegrationOptions
        {
            ContentStart = true,
            Options = new ServerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = true,
            },
            ContentAssemblies = new[]
            {
                typeof(Content.Shared.Entry.EntryPoint).Assembly,
                typeof(Content.Server.Entry.EntryPoint).Assembly,
                typeof(NcStoreTransactionRegressionTest).Assembly,
            },
            ExtraPrototypes = Prototypes,
            OverrideLogHandler = () => logHandler,
        };

        foreach (var (cvar, value) in TestCvars)
            options.CVarOverrides[cvar] = value;

        var server = new RobustIntegrationTest.ServerIntegrationInstance(options);
        await server.WaitIdleAsync();
        return server;
    }

    private static NcStoreListingDef CreateSellListing(string id, string productEntity, int price, int remaining)
    {
        return new NcStoreListingDef
        {
            Id = id,
            Mode = StoreMode.Sell,
            ProductEntity = productEntity,
            RemainingCount = remaining,
            Cost = { [TestCurrency] = price },
        };
    }
}
