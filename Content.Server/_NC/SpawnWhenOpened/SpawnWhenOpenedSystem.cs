using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage;
using Robust.Shared.Random;
using Content.Shared._NC.SpawnWhenOpened;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Destructible;

namespace Content.Server._NC.SpawnWhenOpened;

public sealed partial class SpawnWhenOpenedSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SpawnWhenOpenedComponent, OpenableOpenedEvent>(OnOpened);
        SubscribeLocalEvent<SpawnWhenOpenedComponent, DestructionEventArgs>(OnDestroyed);
    }

    private void OnOpened(EntityUid uid, SpawnWhenOpenedComponent comp, ref OpenableOpenedEvent args)
    {
        if (!comp.IsRepeatable && comp.IsAlreadyOpened)
            return;

        comp.IsAlreadyOpened = true;
        foreach (var ent in EntitySpawnCollection.GetSpawns(comp.Prototypes, _random))
        {
            var item = SpawnNextToOrDrop(ent, uid);

            if (args.User != null)
            {
                var user = args.User ?? default;
                _hands.TryPickupAnyHand(user, item);
            }
        }
    }

    private void OnDestroyed(EntityUid uid, SpawnWhenOpenedComponent comp, ref DestructionEventArgs args)
    {
        if (!comp.IsRepeatable && comp.IsAlreadyOpened)
            return;

        comp.IsAlreadyOpened = true;
        foreach (var ent in EntitySpawnCollection.GetSpawns(comp.Prototypes, _random))
        {
            SpawnNextToOrDrop(ent, uid);
        }
    }
}
