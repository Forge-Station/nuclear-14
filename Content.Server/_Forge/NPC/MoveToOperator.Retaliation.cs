// Forge-Change: forget an unreachable entity target after pathfinding fails.
using Content.Server.NPC.Systems;
using Robust.Shared.Map;

namespace Content.Server.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class MoveToOperator
{
    private void ForgetUnreachableAttacker(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>("Target", out var target, _entManager) ||
            !blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var coordinates, _entManager) ||
            coordinates.EntityId != target)
            return;

        _entManager.System<NPCRetaliationSystem>()
            .ForgetUnreachableAttacker(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), target);
    }
}
