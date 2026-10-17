using System.Linq;
using Content.Server.Chemistry.ReactionEffects;
using Content.Server.EntityEffects.Effects;
using Content.Server.Nutrition.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Kitchen;
using Content.Shared.Nutrition.Components;
using Content.Client.Chemistry.EntitySystems; // Forge-Change: SharedFoodGuideDataSystem лежит в Content.Shared, но в клиентском пространстве имён
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Content.Shared.EntityEffects.Effects.EntitySpawning;
using Content.Shared.Tools.Components;
using Content.Shared.Chemistry.Components;

namespace Content.Server.Nutrition.EntitySystems;

public sealed class FoodGuideDataSystem : SharedFoodGuideDataSystem
{
    public static readonly ProtoId<ReagentPrototype>[] ReagentWhitelist =
    [
        "Nutriment",
        "Vitamin",
        "Protein",
        "UncookedAnimalProteins",
        "Fat",
        "Water"
    ];

    public static readonly string[] ComponentNamesBlacklist = ["HumanoidAppearance",];

    public static readonly string[] SuffixBlacklist = ["debug", "do not map", "admeme",];

    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    private readonly Dictionary<string, List<FoodSourceData>> _sources = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        _player.PlayerStatusChanged += OnPlayerStatusChanged;

        ReloadRecipes();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>()
            && !args.WasModified<MicrowaveMealRecipePrototype>() // Forge-Change
            && !args.WasModified<ReactionPrototype>()
        )
            return;

        ReloadRecipes();
    }

    public void ReloadRecipes()
    {
        // TODO: add this code to the list of known recipes because this is spaghetti
        _sources.Clear();

        // Butcherable and slicable entities
        foreach (var ent in _protoMan.EnumeratePrototypes<EntityPrototype>())
        {
            if (ent.Abstract
                || ent.Components.Any(it => ComponentNamesBlacklist.Contains(it.Key))
                || ent.SetSuffix is {} suffix && SuffixBlacklist.Any(it => suffix.Contains(it, StringComparison.OrdinalIgnoreCase))
            )
                continue;

            if (ent.TryGetComponent<ButcherableComponent>(out var butcherable, _componentFactory))
            {
                var butcheringSource = new FoodButcheringData(ent, butcherable);
                foreach (var butchlet in butcherable.SpawnedEntities)
                {
                    if (butchlet.PrototypeId is null)
                        continue;

                    _sources.GetOrNew(butchlet.PrototypeId).Add(butcheringSource);
                }
            }

            // Forge-Change: нарезка еды стала ToolRefinableComponent.
            if (ent.TryGetComponent<ToolRefinableComponent>(out var refinable, _componentFactory))
            {
                foreach (var slice in refinable.RefineResult)
                {
                    if (slice.PrototypeId is not { } sliceProto)
                        continue;

                    _sources.GetOrNew(sliceProto).Add(new FoodSlicingData(ent, sliceProto, slice.Amount));
                }
            }
        }

        // Recipes
        foreach (var recipe in _protoMan.EnumeratePrototypes<MicrowaveMealRecipePrototype>()) // Forge-Change
        {
            _sources.GetOrNew(recipe.Result).Add(new FoodRecipeData(recipe, _protoMan.Index(recipe.Result).Name));
        }

        // Entity-spawning reactions
        foreach (var reaction in _protoMan.EnumeratePrototypes<ReactionPrototype>())
        {
            foreach (var effect in reaction.Effects)
            {
                // Forge-Change: эффект появления сущности теперь называется SpawnEntity.
                if (effect is not SpawnEntity entEffect)
                    continue;

                _sources.GetOrNew(entEffect.Entity).Add(new FoodReactionData(reaction, entEffect.Entity, entEffect.Number));
            }
        }

        Registry.Clear();

        foreach (var (result, sources) in _sources)
        {
            var proto = _protoMan.Index<EntityPrototype>(result);
            // Forge-Change: состав еды у апстрима лежит в прототипах растворов, а не в
            // компоненте - собираем его из них.
            var composition = proto.TryGetComponent<EdibleComponent>(out var food, _componentFactory)
                              && proto.TryGetComponent<SolutionManagerComponent>(out var manager)
                ? GetComposition(manager, food.Solution)
                : [];

            // We filter out food without whitelisted reagents because well when people look for food they usually expect FOOD and not insulated gloves.
            // And we get insulated and other gloves because they have ButcherableComponent and they are also moth food
            if (!composition.Any(it => ReagentWhitelist.Contains<ProtoId<ReagentPrototype>>(it.Reagent.Prototype)))
                continue;

            // We also limit the number of sources to 10 because it's a huge performance strain to render 500 raw meat recipes.
            var distinctSources = sources.DistinctBy(it => it.Identitier).Take(10);
            var entry = new FoodGuideEntry(result, proto.Name, distinctSources.ToArray(), composition);
            Registry.Add(entry);
        }

        RaiseNetworkEvent(new FoodGuideRegistryChangedEvent(Registry));
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Connected)
            return;

        RaiseNetworkEvent(new FoodGuideRegistryChangedEvent(Registry), args.Session);
    }

    /// <summary>
    /// Forge-Change: содержимое раствора из прототипов сущностей-растворов.
    /// </summary>
    private ReagentQuantity[] GetComposition(SolutionManagerComponent manager, string solutionName)
    {
        if (manager.SolutionEnts is not { } ents)
            return [];

        foreach (var entId in ents)
        {
            if (!_protoMan.TryIndex(entId, out var solutionProto)
                || !solutionProto.TryGetComponent<SolutionComponent>(out var solution, _componentFactory)
                || solution.Id != solutionName)
                continue;

            return solution.Solution.Contents.ToArray();
        }

        return [];
    }
}
