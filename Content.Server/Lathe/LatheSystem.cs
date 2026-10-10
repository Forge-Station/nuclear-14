using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Lathe.Components;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.UserInterface;
using Content.Shared.Database;
using Content.Shared.Emag.Components;
using Content.Shared.Examine;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.ReagentSpeed;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage;
using Content.Shared._Misfits.Crafting; // #Misfits Add: clean blueprint component for workbench crafting
using Content.Shared.Weapons.Ranged.Components;
using JetBrains.Annotations;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Lathe
{
    [UsedImplicitly]
    public sealed class LatheSystem : SharedLatheSystem
    {
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly IPrototypeManager _proto = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly ContainerSystem _container = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSys = default!;
        [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
        [Dependency] private readonly PopupSystem _popup = default!;
        [Dependency] private readonly PuddleSystem _puddle = default!;
        [Dependency] private readonly ReagentSpeedSystem _reagentSpeed = default!;
        [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
        [Dependency] private readonly StackSystem _stack = default!;
        [Dependency] private readonly TransformSystem _transform = default!;

        /// <summary>
        /// Per-tick cache
        /// </summary>
        private readonly List<GasMixture> _environments = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<LatheComponent, GetMaterialWhitelistEvent>(OnGetWhitelist);
            SubscribeLocalEvent<LatheComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<LatheComponent, PowerChangedEvent>(OnPowerChanged);
            SubscribeLocalEvent<LatheComponent, TechnologyDatabaseModifiedEvent>(OnDatabaseModified);
            SubscribeLocalEvent<LatheComponent, ResearchRegistrationChangedEvent>(OnResearchRegistrationChanged);

            SubscribeLocalEvent<LatheComponent, LatheQueueRecipeMessage>(OnLatheQueueRecipeMessage);
            SubscribeLocalEvent<LatheComponent, LatheSyncRequestMessage>(OnLatheSyncRequestMessage);

            SubscribeLocalEvent<LatheComponent, BeforeActivatableUIOpenEvent>((u, c, _) => UpdateUserInterfaceState(u, c));
            SubscribeLocalEvent<LatheComponent, MaterialAmountChangedEvent>(OnMaterialAmountChanged);
            SubscribeLocalEvent<LatheComponent, EntInsertedIntoContainerMessage>(OnStorageContainerModified);
            SubscribeLocalEvent<LatheComponent, EntRemovedFromContainerMessage>(OnStorageContainerModified);
            SubscribeLocalEvent<TechnologyDatabaseComponent, LatheGetRecipesEvent>(OnGetRecipes);
            SubscribeLocalEvent<EmagLatheRecipesComponent, LatheGetRecipesEvent>(GetEmagLatheRecipes);
            SubscribeLocalEvent<LatheHeatProducingComponent, LatheStartPrintingEvent>(OnHeatStartPrinting);
        }
        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<LatheProducingComponent, LatheComponent>();
            while (query.MoveNext(out var uid, out var comp, out var lathe))
            {
                if (lathe.CurrentRecipe == null)
                    continue;

                if (_timing.CurTime - comp.StartTime >= comp.ProductionLength)
                    FinishProducing(uid, lathe);
            }

            var heatQuery = EntityQueryEnumerator<LatheHeatProducingComponent, LatheProducingComponent, TransformComponent>();
            while (heatQuery.MoveNext(out var uid, out var heatComp, out _, out var xform))
            {
                if (_timing.CurTime < heatComp.NextSecond)
                    continue;
                heatComp.NextSecond += TimeSpan.FromSeconds(1);

                var position = _transform.GetGridTilePositionOrDefault((uid, xform));
                _environments.Clear();

                if (_atmosphere.GetTileMixture(xform.GridUid, xform.MapUid, position, true) is { } tileMix)
                    _environments.Add(tileMix);

                if (xform.GridUid != null)
                {
                    var enumerator = _atmosphere.GetAdjacentTileMixtures(xform.GridUid.Value, position, false, true);
                    while (enumerator.MoveNext(out var mix))
                    {
                        _environments.Add(mix);
                    }
                }

                if (_environments.Count > 0)
                {
                    var heatPerTile = heatComp.EnergyPerSecond / _environments.Count;
                    foreach (var env in _environments)
                    {
                        _atmosphere.AddHeat(env, heatPerTile);
                    }
                }
            }
        }

        private void OnGetWhitelist(EntityUid uid, LatheComponent component, ref GetMaterialWhitelistEvent args)
        {
            if (args.Storage != uid)
                return;
            var materialWhitelist = new List<ProtoId<MaterialPrototype>>();
            var recipes = GetAvailableRecipes(uid, component, true);
            foreach (var id in recipes)
            {
                if (!_proto.TryIndex(id, out var proto))
                    continue;
                foreach (var (mat, _) in proto.Materials)
                {
                    if (!materialWhitelist.Contains(mat))
                    {
                        materialWhitelist.Add(mat);
                    }
                }
            }

            var combined = args.Whitelist.Union(materialWhitelist).ToList();
            args.Whitelist = combined;
        }

        [PublicAPI]
        public bool TryGetAvailableRecipes(EntityUid uid, [NotNullWhen(true)] out List<ProtoId<LatheRecipePrototype>>? recipes, [NotNullWhen(true)] LatheComponent? component = null, bool getUnavailable = false)
        {
            recipes = null;
            if (!Resolve(uid, ref component))
                return false;
            recipes = GetAvailableRecipes(uid, component, getUnavailable);
            return true;
        }

        public List<ProtoId<LatheRecipePrototype>> GetAvailableRecipes(EntityUid uid, LatheComponent component, bool getUnavailable = false)
        {
            var ev = new LatheGetRecipesEvent(uid, getUnavailable)
            {
                Recipes = new List<ProtoId<LatheRecipePrototype>>(component.StaticRecipes)
            };
            RaiseLocalEvent(uid, ev);

            AddBlueprintRecipesFromStorage(uid, ev.Recipes);

            return ev.Recipes;
        }

        /// <summary>
        /// #Misfits Add: Scans the workbench's attached storage container for entities
        /// carrying <see cref="BlueprintComponent"/>. For each found blueprint, its
        /// listed recipe IDs are added to the available recipes list. This is the clean
        /// replacement for the removed Stalker14 AddStorageBlueprintRecipes method.
        /// </summary>
        private void AddBlueprintRecipesFromStorage(EntityUid uid, List<ProtoId<LatheRecipePrototype>> recipes)
        {
            if (!TryComp<StorageComponent>(uid, out var storage))
                return;

            foreach (var entity in storage.Container.ContainedEntities)
            {
                if (!TryComp<BlueprintComponent>(entity, out var blueprint))
                    continue;

                foreach (var recipeId in blueprint.Recipes)
                {
                    if (!recipes.Contains(recipeId))
                        recipes.Add(recipeId);
                }
            }
        }

        public static List<ProtoId<LatheRecipePrototype>> GetAllBaseRecipes(LatheComponent component)
        {
            return component.StaticRecipes.Union(component.DynamicRecipes).ToList();
        }

        public bool TryAddToQueue(EntityUid uid, LatheRecipePrototype recipe, LatheComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return false;

            if (!CanProduce(uid, recipe, 1, component))
                return false;

            foreach (var (mat, amount) in recipe.Materials)
            {
                var adjustedAmount = recipe.ApplyMaterialDiscount
                    ? (int) (-amount * component.MaterialUseMultiplier)
                    : -amount;

                _materialStorage.TryChangeMaterialAmount(uid, mat, adjustedAmount);
            }
            component.Queue.Add(recipe);

            return true;
        }

        public bool TryStartProducing(EntityUid uid, LatheComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return false;
            if (component.CurrentRecipe != null || component.Queue.Count <= 0 || !this.IsPowered(uid, EntityManager))
                return false;

            var recipe = component.Queue.First();
            component.Queue.RemoveAt(0);

            var time = _reagentSpeed.ApplySpeed(uid, recipe.CompleteTime) * component.TimeMultiplier;

            var lathe = EnsureComp<LatheProducingComponent>(uid);
            lathe.StartTime = _timing.CurTime;
            lathe.ProductionLength = time;
            component.CurrentRecipe = recipe;

            var ev = new LatheStartPrintingEvent(recipe);
            RaiseLocalEvent(uid, ref ev);

            _audio.PlayPvs(component.ProducingSound, uid);
            UpdateRunningAppearance(uid, true);
            UpdateUserInterfaceState(uid, component);

            if (time == TimeSpan.Zero)
            {
                FinishProducing(uid, component, lathe);
            }
            return true;
        }

        public void FinishProducing(EntityUid uid, LatheComponent? comp = null, LatheProducingComponent? prodComp = null)
        {
            if (!Resolve(uid, ref comp, ref prodComp, false))
                return;

            if (comp.CurrentRecipe != null)
            {
                // #Misfits Add: Debug logging for blueprint crafting
                Log.Info($"FinishProducing: recipe={comp.CurrentRecipe.ID}, result={comp.CurrentRecipe.Result}");

                if (comp.CurrentRecipe.Result is { } resultProto)
                {
                    var result = Spawn(resultProto, Transform(uid).Coordinates);
                    StripCraftedWeaponAmmo(result);
                    Log.Info($"FinishProducing: spawned {resultProto} as {result}");
                    _stack.TryMergeToContacts(result);
                }
                else
                {
                    Log.Warning($"FinishProducing: recipe {comp.CurrentRecipe.ID} has null Result — no entity spawned!");
                }

                if (comp.CurrentRecipe.ResultReagents is { } resultReagents &&
                    comp.ReagentOutputSlotId is { } slotId)
                {
                    var toAdd = new Solution(
                        resultReagents.Select(p => new ReagentQuantity(p.Key.Id, p.Value, null)));

                    // dispense it in the container if we have it and dump it if we don't
                    if (_container.TryGetContainer(uid, slotId, out var container) &&
                        container.ContainedEntities.Count == 1 &&
                        _solution.TryGetFitsInDispenser(container.ContainedEntities.First(), out var solution, out _))
                    {
                        _solution.AddSolution(solution.Value, toAdd);
                    }
                    else
                    {
                        _popup.PopupEntity(Loc.GetString("lathe-reagent-dispense-no-container", ("name", uid)), uid);
                        _puddle.TrySpillAt(uid, toAdd, out _);
                    }
                }
            }

            comp.CurrentRecipe = null;
            prodComp.StartTime = _timing.CurTime;

            if (!TryStartProducing(uid, comp))
            {
                RemCompDeferred(uid, prodComp);
                UpdateUserInterfaceState(uid, comp);
                UpdateRunningAppearance(uid, false);
            }
        }

        /// <summary>
        /// Ensures fabricated guns spawn empty with no inserted magazine or chambered rounds.
        /// </summary>
        private void StripCraftedWeaponAmmo(EntityUid crafted)
        {
            if (!HasComp<GunComponent>(crafted))
                return;

            ClearContainerEntities(crafted, "gun_magazine");
            ClearContainerEntities(crafted, "gun_chamber");
            ClearContainerEntities(crafted, "revolver-ammo");
            ClearContainerEntities(crafted, "ballistic-ammo");

            if (TryComp<BallisticAmmoProviderComponent>(crafted, out var ballistic))
            {
                ballistic.UnspawnedCount = 0;
                ballistic.Entities.Clear();
                Dirty(crafted, ballistic);
            }

            if (TryComp<RevolverAmmoProviderComponent>(crafted, out var revolver))
            {
                for (var i = 0; i < revolver.AmmoSlots.Count; i++)
                {
                    revolver.AmmoSlots[i] = null;
                }

                for (var i = 0; i < revolver.Chambers.Length; i++)
                {
                    revolver.Chambers[i] = null;
                }

                Dirty(crafted, revolver);
            }
        }

        private void ClearContainerEntities(EntityUid uid, string containerId)
        {
            if (!_container.TryGetContainer(uid, containerId, out var container))
                return;

            foreach (var ent in container.ContainedEntities.ToArray())
            {
                Del(ent);
            }
        }

        public void UpdateUserInterfaceState(EntityUid uid, LatheComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            var producing = component.CurrentRecipe ?? component.Queue.FirstOrDefault();

            var state = new LatheUpdateState(GetAvailableRecipes(uid, component), component.Queue, producing);
            _uiSys.SetUiState(uid, LatheUiKey.Key, state);
        }

        private void OnGetRecipes(EntityUid uid, TechnologyDatabaseComponent component, LatheGetRecipesEvent args)
        {
            if (uid != args.Lathe || !TryComp<LatheComponent>(uid, out var latheComponent))
                return;

            foreach (var recipe in latheComponent.DynamicRecipes)
            {
                if (!(args.getUnavailable || component.UnlockedRecipes.Contains(recipe)) || args.Recipes.Contains(recipe))
                    continue;
                args.Recipes.Add(recipe);
            }
        }

        private void GetEmagLatheRecipes(EntityUid uid, EmagLatheRecipesComponent component, LatheGetRecipesEvent args)
        {
            if (uid != args.Lathe || !TryComp<TechnologyDatabaseComponent>(uid, out var technologyDatabase))
                return;
            if (!args.getUnavailable && !HasComp<EmaggedComponent>(uid))
                return;
            foreach (var recipe in component.EmagDynamicRecipes)
            {
                if (!(args.getUnavailable || technologyDatabase.UnlockedRecipes.Contains(recipe)) || args.Recipes.Contains(recipe))
                    continue;
                args.Recipes.Add(recipe);
            }
            foreach (var recipe in component.EmagStaticRecipes)
            {
                args.Recipes.Add(recipe);
            }
        }

        private void OnHeatStartPrinting(EntityUid uid, LatheHeatProducingComponent component, LatheStartPrintingEvent args)
        {
            component.NextSecond = _timing.CurTime;
        }

        private void OnMaterialAmountChanged(EntityUid uid, LatheComponent component, ref MaterialAmountChangedEvent args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        private void OnStorageContainerModified(EntityUid uid, LatheComponent component, ref EntInsertedIntoContainerMessage args)
        {
            // #Misfits Fix: Refresh UI state when physical material entities
            // (canProduce / available material amounts change) are inserted into storage.
            // #Misfits Add: Also refresh when a blueprint is inserted so newly unlocked
            // recipes appear immediately.
            if (!HasComp<MaterialComponent>(args.Entity) && !HasComp<BlueprintComponent>(args.Entity))
                return;

            // #Misfits Fix: Refresh the material pool whitelist whenever a blueprint is
            // added/removed (unlocks/locks its ingredients) or a material entity is inserted.
            // Without this the whitelist stays as it was at MapInit (static/dynamic-only),
            // so blueprint-only materials (e.g. N14IngotIron) are rejected by
            // CanChangeMaterialAmount and end up dumped into plain storage instead of the pool.
            _materialStorage.UpdateMaterialWhitelist(uid);
            UpdateUserInterfaceState(uid, component);
        }

        private void OnStorageContainerModified(EntityUid uid, LatheComponent component, ref EntRemovedFromContainerMessage args)
        {
            // #Misfits Fix: Same as insertion - refresh when material
            // entities are removed so available amounts are recalculated.
            // #Misfits Add: Also refresh when a blueprint is removed so its recipes
            // disappear from the available list.
            if (!HasComp<MaterialComponent>(args.Entity) && !HasComp<BlueprintComponent>(args.Entity))
                return;

            // #Misfits Fix: Keep the material pool whitelist in sync with storage content
            // (see reasoning in the insertion handler above).
            _materialStorage.UpdateMaterialWhitelist(uid);
            UpdateUserInterfaceState(uid, component);
        }

        /// <summary>
        /// Initialize the UI and appearance.
        /// Appearance requires initialization or the layers break
        /// </summary>
        private void OnMapInit(EntityUid uid, LatheComponent component, MapInitEvent args)
        {
            _appearance.SetData(uid, LatheVisuals.IsInserting, false);
            _appearance.SetData(uid, LatheVisuals.IsRunning, false);

            _materialStorage.UpdateMaterialWhitelist(uid);
        }

        /// <summary>
        /// Sets the machine sprite to either play the running animation
        /// or stop.
        /// </summary>
        private void UpdateRunningAppearance(EntityUid uid, bool isRunning)
        {
            _appearance.SetData(uid, LatheVisuals.IsRunning, isRunning);
        }

        private void OnPowerChanged(EntityUid uid, LatheComponent component, ref PowerChangedEvent args)
        {
            if (!args.Powered)
            {
                RemComp<LatheProducingComponent>(uid);
                UpdateRunningAppearance(uid, false);
            }
            else if (component.CurrentRecipe != null)
            {
                EnsureComp<LatheProducingComponent>(uid);
                TryStartProducing(uid, component);
            }
        }

        private void OnDatabaseModified(EntityUid uid, LatheComponent component, ref TechnologyDatabaseModifiedEvent args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        private void OnResearchRegistrationChanged(EntityUid uid, LatheComponent component, ref ResearchRegistrationChangedEvent args)
        {
            UpdateUserInterfaceState(uid, component);
        }

        protected override bool HasRecipe(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
        {
            return GetAvailableRecipes(uid, component).Contains(recipe.ID);
        }

        #region UI Messages

        private void OnLatheQueueRecipeMessage(EntityUid uid, LatheComponent component, LatheQueueRecipeMessage args)
        {
            // #Misfits Add: Debug logging for blueprint crafting pipeline
            Log.Info($"LatheQueueRecipe: actor={args.Actor}, recipe={args.ID}, qty={args.Quantity}");

            if (_proto.TryIndex(args.ID, out LatheRecipePrototype? recipe))
            {
                // Convert raw material entities in storage into the material pool before queuing.
                // This avoids availability/consumption mismatches from physical material stacks.
                NormalizeStoredMaterialsToPool(uid, args.Actor);

                var count = 0;
                for (var i = 0; i < args.Quantity; i++)
                {
                    if (TryAddToQueue(uid, recipe, component))
                        count++;
                    else
                    {
                        if (i == 0)
                        {
                            var hasRecipe = HasRecipe(uid, recipe, component);
                            var canProduce = CanProduce(uid, recipe, 1, component);
                            var missing = string.Join(", ",
                                recipe.Materials.Select(m =>
                                {
                                    var needed = recipe.ApplyMaterialDiscount
                                        ? (int) MathF.Ceiling(m.Value * component.MaterialUseMultiplier)
                                        : m.Value;
                                    var available = _materialStorage.GetMaterialAmount(uid, m.Key);
                                    var shortfall = Math.Max(0, needed - available);
                                    return shortfall > 0 ? $"{m.Key}:{shortfall}" : null;
                                }).Where(x => x != null)!);

                            Log.Warning($"LatheQueueRecipe: FAILED to queue {args.ID} for actor={args.Actor}. hasRecipe={hasRecipe}, canProduce={canProduce}, Missing={missing}");
                            _popup.PopupEntity(Loc.GetString("lathe-blueprint-queue-failed"), uid, args.Actor);
                        }

                        break;
                    }
                }
                Log.Info($"LatheQueueRecipe: queued {count}/{args.Quantity} of {args.ID}");
                if (count > 0)
                {
                    _adminLogger.Add(LogType.Action,
                        LogImpact.Low,
                        $"{ToPrettyString(args.Actor):player} queued {count} {GetRecipeName(recipe)} at {ToPrettyString(uid):lathe}");
                }
            }
            TryStartProducing(uid, component);
            UpdateUserInterfaceState(uid, component);
        }

        /// <summary>
        /// Converts material entities currently inside attached storage into the machine material pool.
        /// This makes queue-time consumption deterministic and avoids physical-stack edge cases.
        /// </summary>
        private void NormalizeStoredMaterialsToPool(EntityUid uid, EntityUid actor)
        {
            if (!TryComp<StorageComponent>(uid, out var storage))
                return;

            foreach (var ent in storage.Container.ContainedEntities.ToArray())
            {
                if (!HasComp<MaterialComponent>(ent) || !HasComp<PhysicalCompositionComponent>(ent))
                    continue;

                _materialStorage.TryInsertMaterialEntity(actor, ent, uid);
            }
        }

        private void OnLatheSyncRequestMessage(EntityUid uid, LatheComponent component, LatheSyncRequestMessage args)
        {
            UpdateUserInterfaceState(uid, component);
        }
        #endregion
    }
}
