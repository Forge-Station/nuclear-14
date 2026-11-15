using Content.Shared._Forge.FactionResearch;
using Content.Shared._Misfits.Crafting;
using Content.Shared._Misfits.FactionResearch.Prototypes;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.FactionResearch;

/// <summary>
/// Rolls the recipe of a <see cref="RandomBlueprintComponent"/> blueprint on map init,
/// then mirrors it into the workbench-facing <see cref="BlueprintComponent"/>.
/// </summary>
public sealed class RandomBlueprintSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomBlueprintComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, RandomBlueprintComponent component, MapInitEvent args)
    {
        if (component.Recipes is { Count: > 0 })
            return;

        if (!_proto.TryIndex(component.Pool, out RandomResearchSheetPrototype? pool) || pool.Recipes.Count == 0)
            return;

        var rolled = new List<ProtoId<LatheRecipePrototype>> { _random.Pick(pool.Recipes) };
        component.Recipes = rolled;
        Dirty(uid, component);

        var blueprint = EnsureComp<BlueprintComponent>(uid);
        blueprint.Recipes = rolled;
        Dirty(uid, blueprint);
    }
}
