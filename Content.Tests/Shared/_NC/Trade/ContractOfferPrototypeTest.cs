using System;
using System.Collections.Generic;
using Content.Shared._NC.Trade;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Tests.Shared._NC.Trade;

[TestFixture]
[TestOf(typeof(NcContractOfferPoolPrototype))]
[TestOf(typeof(StoreContractsPresetPrototype))]
public sealed class ContractOfferPrototypeTest : ContentUnitTest
{
    private static readonly ProtoId<NcContractOfferPoolPrototype> TestOfferPoolId = "TestOfferPool";
    private static readonly ProtoId<StoreContractsPresetPrototype> TestContractsPresetId = "TestContractsPreset";

    private IPrototypeManager _prototypeManager = default!;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _prototypeManager.Initialize();
        _prototypeManager.LoadString(Prototypes);
        _prototypeManager.ResolveResults();
    }

    [Test]
    public void OfferPoolLoadsAndEntryWeightDefaultsToOne()
    {
        var pool = _prototypeManager.Index(TestOfferPoolId);

        Assert.That(pool.Name, Is.EqualTo("Test pool"));
        Assert.That(pool.Order, Is.EqualTo(25));
        Assert.That(pool.Color, Is.EqualTo("#C9A45A"));
        Assert.That(pool.Entries, Has.Count.EqualTo(1));
        Assert.That(pool.Entries[0].Type, Is.EqualTo(NcContractOfferType.Supply));
        Assert.That(pool.Entries[0].Id, Is.EqualTo("TestSupplyContract"));
        Assert.That(pool.Entries[0].Weight, Is.EqualTo(1));
    }

    [Test]
    public void ContractOffersGroupDefaultsAreStable()
    {
        var preset = _prototypeManager.Index(TestContractsPresetId);

        Assert.That(preset.ContractOffers, Is.Not.Null);
        Assert.That(preset.ContractOffers!.MaxVisible, Is.EqualTo(4));
        Assert.That(preset.ContractOffers.Groups, Has.Count.EqualTo(1));

        var group = preset.ContractOffers.Groups[0];
        Assert.That(group.Pool.Id, Is.EqualTo("TestOfferPool"));
        Assert.That(group.MinVisible, Is.EqualTo(0));
        Assert.That(group.MaxVisible, Is.EqualTo(1));
        Assert.That(group.FillWeight, Is.EqualTo(1));
    }

    [Test]
    public void ContractClientDataCarriesOfferSortMetadata()
    {
        var contract = new ContractClientData
        {
            Id = "TestSupplyContract",
            Name = "Alpha",
            OfferPoolId = "TestOfferPool",
            OfferPoolName = "Test pool",
            OfferPoolOrder = 25,
            OfferPoolColor = "#C9A45A",
        };

        Assert.That(contract.OfferPoolId, Is.EqualTo("TestOfferPool"));
        Assert.That(contract.OfferPoolName, Is.EqualTo("Test pool"));
        Assert.That(contract.OfferPoolOrder, Is.EqualTo(25));
        Assert.That(contract.OfferPoolColor, Is.EqualTo("#C9A45A"));
    }

    [Test]
    public void OfferSortUsesPoolOrderBeforeNameAndId()
    {
        var contracts = new List<ContractClientData>
        {
            new() { Id = "B", Name = "Beta", OfferPoolOrder = 30 },
            new() { Id = "A", Name = "Alpha", OfferPoolOrder = 10 },
            new() { Id = "C", Name = "Alpha", OfferPoolOrder = 10 },
        };

        contracts.Sort(static (left, right) =>
        {
            var poolOrder = left.OfferPoolOrder.CompareTo(right.OfferPoolOrder);
            if (poolOrder != 0)
                return poolOrder;

            var name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            if (name != 0)
                return name;

            return string.CompareOrdinal(left.Id, right.Id);
        });

        Assert.That(contracts[0].Id, Is.EqualTo("A"));
        Assert.That(contracts[1].Id, Is.EqualTo("C"));
        Assert.That(contracts[2].Id, Is.EqualTo("B"));
    }

    private const string Prototypes = @"
- type: ncContractOfferPool
  id: TestOfferPool
  name: Test pool
  order: 25
  color: ""#C9A45A""
  entries:
  - type: Supply
    id: TestSupplyContract

- type: storeContractsPreset
  id: TestContractsPreset
  contractOffers:
    maxVisible: 4
    groups:
    - pool: TestOfferPool
";
}
