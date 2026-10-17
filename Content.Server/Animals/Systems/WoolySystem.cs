using Content.Server.Animals.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Nutrition;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Timing;
using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Nutrition;

namespace Content.Server.Animals.Systems;

/// <summary>
///     Gives ability to produce fiber reagents, produces endless if the 
///     owner has no HungerComponent
/// </summary>
public sealed class WoolySystem : EntitySystem
{
    [Dependency] private readonly SatiationSystem _satiation = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SolutionContainerSystem _solutionContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WoolyComponent, IngestibleEvent>(OnIngestibleAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WoolyComponent>();
        var now = _timing.CurTime;
        while (query.MoveNext(out var uid, out var wooly))
        {
            if (now < wooly.NextGrowth)
                continue;

            wooly.NextGrowth = now + wooly.GrowthDelay;

            if (_mobState.IsDead(uid))
                continue;

            // Actually there is food digestion so no problem with instant reagent generation "OnFeed"
            // Голод стал сытостью: без SatiationComponent животное производит
            // шерсть бесконечно, как и раньше без HungerComponent.
            if (HasComp<SatiationComponent>(uid))
            {
                // Is there enough nutrition to produce reagent?
                if (_satiation.GetValueOrNull(uid, SatiationSystem.Hunger) is not { } hunger
                    || hunger < wooly.HungerUsage)
                    continue;

                _satiation.ModifyValue(uid, SatiationSystem.Hunger, -wooly.HungerUsage);
            }

            if (!_solutionContainer.ResolveSolution(uid, wooly.SolutionName, ref wooly.Solution))
                continue;

            _solutionContainer.TryAddReagent(wooly.Solution.Value, wooly.ReagentId, wooly.Quantity, out _);
        }
    }

    private void OnIngestibleAttempt(Entity<WoolyComponent> ent, ref IngestibleEvent args)
    {
        // Апстрим убрал отменяемое "перед съедением", поэтому козу просто
        // нельзя съесть - моли её больше не удалят.
        args.Cancelled = true;
    }
}
