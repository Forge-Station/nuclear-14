using System.Collections.Generic;
using System.Collections;
using System.Reflection;
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
    private const string TestPlaceableCurrency = "NcTradeTestPlaceableCredit";
    private const string TestPlaceableCurrencyStack = "NcTradeTestPlaceableCreditStack";
    private const string TestSaleItem = "NcTradeTestSaleItem";
    private const string TestStackedSaleItem = "NcTradeTestStackedSaleItem";
    private const string TestAltStackedSaleItem = "NcTradeTestAltStackedSaleItem";
    private const string TestAbstractProduct = "NcTradeTestAbstractProduct";
    private const string TestStaticTaggedItem = "NcTradeTestStaticTaggedItem";
    private const string TestRuntimeTaggedItem = "NcTradeTestRuntimeTaggedItem";
    private const string TestStaticTagTarget = "NcTradeTestStaticTagTarget";
    private const string TestRuntimeTagTarget = "NcTradeTestRuntimeTagTarget";
    private const string TestStackMatcher = "NcTradeTestStackMatcher";
    private const string TestProofItem = "NcTradeTestProofItem";

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

- type: ncTradeTag
  id: NcTradeTestStaticTagTarget
  name: static tag target
  description: prototype-declared tag target
  tag: NcTradeTestStaticTag
  icon: NcTradeTestStaticTaggedItem

- type: ncTradeTag
  id: NcTradeTestRuntimeTagTarget
  name: runtime tag target
  description: runtime-only tag target
  tag: NcTradeTestRuntimeTag
  icon: NcTradeTestRuntimeTaggedItem

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

- type: stack
  id: NcTradeTestPlaceableCredit
  name: placeable test credit
  spawn: NcTradeTestPlaceableCreditStack
  maxCount: 10

- type: entity
  parent: BaseItem
  id: NcTradeTestPlaceableCreditStack
  components:
  - type: Stack
    stackType: NcTradeTestPlaceableCredit
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
  id: NcTradeTestAltStackedSaleItem
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

- type: entity
  parent: BaseItem
  id: NcTradeTestProofItem

- type: ncMatcher
  id: NcTradeTestStackMatcher
  name: test stack matcher
  items:
  - NcTradeTestStackedSaleItem
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
    public async Task RewardCurrencyTransactionWaitsForPreCommitToFreeHands()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var inventory = entMan.System<NcStoreInventorySystem>();
        var logic = entMan.System<NcStoreLogicSystem>();

        EntityUid user = default;
        EntityUid heldA = default;
        EntityUid heldB = default;

        await server.WaitPost(() =>
        {
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);

            heldA = entMan.SpawnEntity(TestSaleItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(user, heldA), Is.True);

            heldB = entMan.SpawnEntity(TestSaleItem, MapCoordinates.Nullspace);
            Assert.That(hands.TryPickupAnyHand(user, heldB), Is.True);
        });

        await server.WaitRunTicks(5);

        var rewards = new List<ContractRewardData>
        {
            new(StoreRewardType.Currency, TestPlaceableCurrency, 2)
        };

        await server.WaitAssertion(() =>
        {
            var ok = logic.TryExecuteRewardListWithPreCommit(
                user,
                rewards,
                "Claim",
                () =>
                {
                    entMan.DeleteEntity(heldA);
                    entMan.DeleteEntity(heldB);
                    return null;
                },
                out var reason);

            Assert.That(ok, Is.True, reason);
            Assert.That(entMan.EntityExists(heldA), Is.False);
            Assert.That(entMan.EntityExists(heldB), Is.False);

            var snapshot = inventory.BuildInventorySnapshot(user);
            Assert.That(snapshot.StackTypeCounts.TryGetValue(TestPlaceableCurrency, out var balance), Is.True);
            Assert.That(balance, Is.EqualTo(2));
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
    public async Task MassSellMatcherPreviewCountsMatchingStackTypes()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var stack = entMan.System<SharedStackSystem>();
        var inventory = entMan.System<NcStoreInventorySystem>();
        var logic = entMan.System<NcStoreLogicSystem>();

        EntityUid storeUid = default;
        EntityUid container = default;
        EntityUid stacked = default;
        NcStoreComponent store = default!;

        await server.WaitPost(() =>
        {
            storeUid = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            container = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            store = new NcStoreComponent();

            stacked = entMan.SpawnEntity(TestAltStackedSaleItem, MapCoordinates.Nullspace);
            stack.SetCount(stacked, 4);
            Assert.That(hands.TryPickupAnyHand(container, stacked), Is.True);

            store.Listings.Add(new NcStoreListingDef
            {
                Id = "mass-sell-stack-matcher",
                Mode = StoreMode.Sell,
                ProductEntity = TestStackMatcher,
                MatchMode = PrototypeMatchMode.Matcher,
                RemainingCount = 10,
                Cost = { [TestPlaceableCurrency] = 1 },
            });
            store.RebuildListingIndex();
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var items = new List<EntityUid>();
            inventory.ScanInventoryItems(container, items);
            var plan = logic.ComputeMassSellPlanFromCachedItems(storeUid, store, container, items);

            Assert.That(plan.UnitsByListingId.TryGetValue("mass-sell-stack-matcher", out var units), Is.True);
            Assert.That(units, Is.EqualTo(4));
            Assert.That(plan.IncomeByCurrency.TryGetValue(TestPlaceableCurrency, out var income), Is.True);
            Assert.That(income, Is.EqualTo(4));
        });

    }

    [Test]
    public async Task ObjectiveProofConsumeJournalRollsBackStagedProofRemoval()
    {
        using var server = await StartServer();

        var entMan = server.ResolveDependency<IEntityManager>();
        var hands = entMan.System<SharedHandsSystem>();
        var contracts = entMan.System<NcContractSystem>();

        EntityUid store = default;
        EntityUid user = default;
        EntityUid proof = default;
        const string contractId = "objective-proof-journal";

        await server.WaitPost(() =>
        {
            store = entMan.SpawnEntity(TestSaleItem, MapCoordinates.Nullspace);
            user = entMan.SpawnEntity("MobHumanDummy", MapCoordinates.Nullspace);
            proof = entMan.SpawnEntity(TestProofItem, MapCoordinates.Nullspace);

            var proofComp = entMan.EnsureComponent<NcContractProofComponent>(proof);
            proofComp.Store = store;
            proofComp.ContractId = contractId;
            proofComp.ProofToken = "proof-token";

            Assert.That(hands.TryPickupAnyHand(user, proof), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var key = (store, contractId);
            var state = InvokePrivate<object>(contracts, "GetOrCreateObjectiveRuntimeState", key);
            var stateType = state.GetType();
            var proofEntityField = stateType.GetField("ProofEntity", BindingFlags.Instance | BindingFlags.Public)!;
            var proofTokenField = stateType.GetField("ProofToken", BindingFlags.Instance | BindingFlags.Public)!;

            proofEntityField.SetValue(state, proof);
            proofTokenField.SetValue(state, "proof-token");

            var runtime = GetPrivateField<object>(contracts, "_objectiveRuntime");
            var byProof = (IDictionary) runtime.GetType()
                .GetField("ByProof", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(runtime)!;
            byProof[proof] = key;

            var contract = new ContractServerData
            {
                Id = contractId,
                Config = new ContractObjectiveConfigData
                {
                    ProofPrototype = TestProofItem
                }
            };

            var journalType = typeof(NcContractSystem).GetNestedType(
                "ObjectiveConsumeJournal",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var journal = Activator.CreateInstance(journalType, nonPublic: true)!;
            var consume = typeof(NcContractSystem).GetMethod(
                "TryConsumeObjectiveProof",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var args = new object[] { store, user, contractId, contract, journal, null! };
            var ok = (bool) consume.Invoke(contracts, args)!;

            Assert.That(ok, Is.True);
            Assert.That(entMan.EntityExists(proof), Is.True);
            Assert.That(proofEntityField.GetValue(state), Is.Null);
            Assert.That(byProof.Contains(proof), Is.False);

            InvokePrivateVoid(contracts, "RollbackObjectiveConsumeJournal", journal);

            Assert.That(entMan.EntityExists(proof), Is.True);
            Assert.That(proofEntityField.GetValue(state), Is.EqualTo(proof));
            Assert.That(byProof.Contains(proof), Is.True);
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
    public async Task TagMatchModeUsesPrototypeTagsNotRuntimeTags()
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
            Assert.That(inventory.GetOwnedFromSnapshot(snapshot, TestStaticTagTarget, PrototypeMatchMode.Tag), Is.EqualTo(1));
            Assert.That(inventory.GetOwnedFromRootCached(user, TestStaticTagTarget, PrototypeMatchMode.Tag), Is.EqualTo(1));
            Assert.That(inventory.GetOwnedFromSnapshot(snapshot, TestRuntimeTagTarget, PrototypeMatchMode.Tag), Is.EqualTo(0));
            Assert.That(inventory.GetOwnedFromRootCached(user, TestRuntimeTagTarget, PrototypeMatchMode.Tag), Is.EqualTo(0));
            Assert.That(inventory.GetOwnedFromSnapshot(snapshot, "NcTradeTestStaticTag", PrototypeMatchMode.Tag), Is.EqualTo(0));
            Assert.That(inventory.PrototypeHasTag(TestRuntimeTaggedItem, "NcTradeTestRuntimeTag"), Is.False);
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

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}' on {instance.GetType()}.");
        return (T) field!.GetValue(instance)!;
    }

    private static T InvokePrivate<T>(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing private method '{methodName}' on {instance.GetType()}.");
        return (T) method!.Invoke(instance, args)!;
    }

    private static void InvokePrivateVoid(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing private method '{methodName}' on {instance.GetType()}.");
        method!.Invoke(instance, args);
    }
}
