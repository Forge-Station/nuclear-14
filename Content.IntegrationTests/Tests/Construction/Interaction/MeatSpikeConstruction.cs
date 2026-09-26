using Content.IntegrationTests.Tests.Interaction;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class MeatSpikeConstruction : InteractionTest
{
    [Test]
    public async Task ConstructMeatSpike()
    {
        await StartConstruction("MeatSpike");
        await InteractUsing(Steel, 15);
        ClientAssertPrototype("KitchenSpikeFrame", Target);
        await InteractUsing(Rod, 5);
        await Interact(Weld, Wrench);
        AssertPrototype("KitchenSpike");
    }
}
