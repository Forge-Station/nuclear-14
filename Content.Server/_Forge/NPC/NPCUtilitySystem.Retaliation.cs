// Forge-Change: check remembered attackers through the usual utility considerations.
using Content.Server.NPC.Queries;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCUtilitySystem
{
    [Dependency] private readonly NPCRetaliationSystem _retaliation = default!;

    private bool CanSelectRetaliationTarget(NPCBlackboard blackboard, EntityUid target, UtilityQueryPrototype query)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!TryComp<TransformComponent>(owner, out var xform) ||
            !TryComp<TransformComponent>(target, out var targetXform) ||
            !xform.Coordinates.TryDistance(EntityManager, _transform, targetXform.Coordinates, out var distance))
            return false;

        foreach (var consideration in query.Considerations)
        {
            var score = GetScore(blackboard, target, consideration, distance);
            if (GetScore(consideration.Curve, score) <= 0f)
                return false;
        }

        return true;
    }
}
