using Content.Server.Mining.Components;
using Content.Shared.Destructible;
using Content.Shared.Mining;
using Content.Shared.Random;
using Content.Shared.Random.Helpers;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Mining;

/// <summary>
/// This handles creating ores when the entity is destroyed.
/// </summary>
public sealed class MiningSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreVeinComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OreVeinComponent, DestructionEventArgs>(OnDestruction);
    }

    private void OnDestruction(EntityUid uid, OreVeinComponent component, DestructionEventArgs args)
    {
        if (component.CurrentOre == null)
            return;

        var proto = _proto.Index<OrePrototype>(component.CurrentOre);

        if (proto.OreEntity == null)
            return;

        // Forge: scale the ore yield by the stack size so a whole stack of ore pays out fully.
        var stackCount = 1;
        if (TryComp<StackComponent>(uid, out var stackComp))
            stackCount = Math.Max(1, stackComp.Count);

        var coords = Transform(uid).Coordinates;
        for (var i = 0; i < stackCount; i++)
        {
            var toSpawn = _random.Next(proto.MinOreYield, proto.MaxOreYield);
            for (var j = 0; j < toSpawn; j++)
            {
                Spawn(proto.OreEntity, coords.Offset(_random.NextVector2(0.2f)));
            }
        }
    }

    private void OnMapInit(EntityUid uid, OreVeinComponent component, MapInitEvent args)
    {
        if (component.CurrentOre != null || component.OreRarityPrototypeId == null || !_random.Prob(component.OreChance))
            return;

        component.CurrentOre = _proto.Index<WeightedRandomOrePrototype>(component.OreRarityPrototypeId).Pick(_random);
    }
}
