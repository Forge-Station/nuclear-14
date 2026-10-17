using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;

namespace Content.Server.NPC.HTN.Preconditions;

/// <summary>
/// Returns true if the owner is at or below the given hunger threshold.
/// </summary>
/// <remarks>
/// Forge-Change: голод стал сытостью, и у неё нет перечисления уровней - порог
/// задаётся именем из прототипа типа сытости, как в остальных апстримных полях.
/// </remarks>
public sealed partial class HungryPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    [DataField(required: true)]
    public SatiationValue MinHungerState = "Starving";

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager)
            || !_entManager.TryGetComponent<SatiationComponent>(owner, out var satiation))
            return false;

        // Значение возвращается, только если текущая сытость не выше порога.
        return _entManager.System<SatiationSystem>().TryGetValueByThreshold(
            (owner, satiation),
            SatiationSystem.Hunger,
            new Dictionary<SatiationValue, bool> { [MinHungerState] = true },
            out _,
            out _,
            out _);
    }
}
