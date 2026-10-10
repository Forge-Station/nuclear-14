using System.Collections.Generic;
using System.Linq;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Item;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._Forge.Weapons;

[TestFixture]
public sealed class SpentCartridgeTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: CasingTestRevolver
  components:
  - type: Appearance
  - type: RevolverAmmoProvider
    capacity: 2
    proto: CartridgePistol
    soundEject: null

- type: entity
  id: CasingTestGun
  components:
  - type: Gun
    soundGunshot: null

- type: entity
  id: CasingTestBoltGun
  components:
  - type: Appearance
  - type: ChamberMagazineAmmoProvider
    boltClosed: true
    soundBoltOpened: null
";

    [TestCase(false)]
    [TestCase(true)]
    public async Task RevolverEjectsSpentVisualAndPreservesLiveRound(bool instantiated)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var client = pair.Client;
        EntityUid revolver = default;
        EntityUid spent = default;
        EntityUid live = default;

        await server.WaitPost(() =>
        {
            var observer = server.EntMan.SpawnEntity(null, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, observer);
            revolver = server.EntMan.SpawnEntity("CasingTestRevolver", map.GridCoords);
            var provider = server.EntMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
            provider.Chambers[0] = false;
            provider.Chambers[1] = true;
            if (!instantiated)
                return;

            spent = server.EntMan.SpawnEntity("CartridgePistol", map.GridCoords);
            live = server.EntMan.SpawnEntity("CartridgePistol", map.GridCoords);
            server.EntMan.GetComponent<CartridgeAmmoComponent>(spent).Spent = true;
            provider.AmmoSlots[0] = spent;
            provider.AmmoSlots[1] = live;
            var containers = server.System<SharedContainerSystem>();
            containers.Insert(spent, provider.AmmoContainer);
            containers.Insert(live, provider.AmmoContainer);
        });
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            var provider = server.EntMan.GetComponent<RevolverAmmoProviderComponent>(revolver);
            server.System<GunSystem>().EmptyRevolver(revolver, provider);
            Assert.That(provider.AmmoSlots, Is.All.Null);
            Assert.That(provider.Chambers, Is.All.Null);
        });
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            var cartridges = server.EntMan.EntityQuery<CartridgeAmmoComponent>().ToArray();
            Assert.That(cartridges.Length, Is.EqualTo(1));
            Assert.That(cartridges[0].Spent, Is.False);
            Assert.That(server.EntMan.HasComponent<ItemComponent>(cartridges[0].Owner), Is.True);
            Assert.That(server.EntMan.HasComponent<PhysicsComponent>(cartridges[0].Owner), Is.True);
            if (instantiated)
            {
                Assert.That(server.EntMan.Deleted(spent), Is.True);
                Assert.That(server.EntMan.Deleted(live), Is.False);
            }
            Assert.That(CasingVisuals(server.EntMan), Is.Empty);
        });
        await client.WaitPost(() =>
        {
            var visuals = CasingVisuals(client.EntMan);
            Assert.That(visuals.Length, Is.EqualTo(1), "Server confirmation must create exactly one local casing.");
            var visual = visuals[0];
            Assert.That(client.EntMan.IsClientSide(visual), Is.True);
            Assert.That(client.EntMan.HasComponent<PhysicsComponent>(visual), Is.False);
            Assert.That(client.EntMan.HasComponent<ItemComponent>(visual), Is.False);
            Assert.That(client.EntMan.HasComponent<TimedDespawnComponent>(visual), Is.True);
            var sprite = client.EntMan.GetComponent<Robust.Client.GameObjects.SpriteComponent>(visual);
            Assert.That(sprite[0].RsiState.Name, Is.EqualTo("base-spent"));
        });

        await pair.RunSeconds(11);
        await client.WaitPost(() => Assert.That(CasingVisuals(client.EntMan), Is.Empty));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShootingDoesNotAccumulateServerCasings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var casings = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var gun = server.EntMan.SpawnEntity("CasingTestGun", map.GridCoords);
            var gunComponent = server.EntMan.GetComponent<GunComponent>(gun);
            for (var i = 0; i < 20; i++)
            {
                var casing = server.EntMan.SpawnEntity("CartridgePistol", map.GridCoords);
                casings.Add(casing);
                var cartridge = server.EntMan.GetComponent<CartridgeAmmoComponent>(casing);
                server.System<GunSystem>().Shoot(gun, gunComponent, new List<(EntityUid?, IShootable)> { (casing, cartridge) },
                    map.GridCoords, map.GridCoords.Offset(new System.Numerics.Vector2(5, 0)), out _);
                Assert.That(server.EntMan.IsQueuedForDeletion(casing), Is.True);
            }
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() =>
        {
            Assert.That(casings.All(uid => server.EntMan.Deleted(uid)), Is.True);
            Assert.That(server.EntMan.EntityQuery<CartridgeAmmoComponent>(), Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OpeningBoltDeletesOnlySpentCartridge(bool spent)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        EntityUid cartridge = default;
        await server.WaitPost(() =>
        {
            var gun = server.EntMan.SpawnEntity("CasingTestBoltGun", map.GridCoords);
            cartridge = server.EntMan.SpawnEntity("CartridgePistol", map.GridCoords);
            server.EntMan.GetComponent<CartridgeAmmoComponent>(cartridge).Spent = spent;
            var containers = server.System<SharedContainerSystem>();
            var chamber = containers.EnsureContainer<ContainerSlot>(gun, "gun_chamber");
            containers.Insert(cartridge, chamber);
            var provider = server.EntMan.GetComponent<ChamberMagazineAmmoProviderComponent>(gun);
            server.System<GunSystem>().SetBoltClosed(gun, provider, false);
            Assert.That(chamber.ContainedEntity, Is.Null);
            Assert.That(server.EntMan.IsQueuedForDeletion(cartridge), Is.EqualTo(spent));
        });
        await pair.RunTicksSync(5);
        await server.WaitPost(() => Assert.That(server.EntMan.Deleted(cartridge), Is.EqualTo(spent)));
        await pair.CleanReturnAsync();
    }

    private static EntityUid[] CasingVisuals(IEntityManager entities) => entities.EntityQuery<MetaDataComponent>()
        .Where(meta => meta.EntityPrototype?.ID == "N14SpentCartridgeVisual")
        .Select(meta => meta.Owner).ToArray();
}
