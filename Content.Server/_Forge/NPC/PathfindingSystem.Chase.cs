// Forge-Change: reject disproportionately long routes around barriers when NPCs chase players.
using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared._Forge.NPC;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>
    /// A distant player on an open road can be chased, but a short distance along grid
    /// axes must not turn into a long trip around an impassable barrier.
    /// </summary>
    public bool ExceedsPlayerChaseDetour(EntityUid uid, EntityCoordinates start, EntityCoordinates target,
        IReadOnlyList<PathPoly> route)
    {
        if (!HasComp<ProximityNPCComponent>(uid) || !HasComp<ActorComponent>(target.EntityId) || route.Count == 0)
            return false;

        var maxDetour = _configManager.GetCVar(CCVars.NPCMaxPlayerChaseDetour);
        if (maxDetour < 0f)
            return false;

        var startMap = start.ToMap(EntityManager, _transform);
        var targetMap = target.ToMap(EntityManager, _transform);
        if (startMap.MapId != targetMap.MapId)
            return false;

        // The graph connects cardinal neighbours. Measure both lengths along its local
        // axes, otherwise an unobstructed staircase looks like a detour on a diagonal.
        var graph = route[0].GraphUid;
        if (!TryComp<TransformComponent>(graph, out var graphTransform) || graphTransform.MapID != startMap.MapId)
            return false;

        // Portals may join differently oriented graphs; there is no single cardinal
        // baseline for such a route, so leave it to normal steering.
        foreach (var node in route)
        {
            if (node.GraphUid != graph)
                return false;
        }

        var startPosition = _transform.ToCoordinates((graph, graphTransform), startMap).Position;
        var targetPosition = _transform.ToCoordinates((graph, graphTransform), targetMap).Position;
        var directDistance = CardinalDistance(startPosition, targetPosition);
        var routeDistance = 0f;
        var previous = startPosition;

        foreach (var node in route)
        {
            var waypoint = node.Coordinates.Position;
            routeDistance += CardinalDistance(previous, waypoint);
            if (routeDistance > directDistance + maxDetour)
                return true;

            previous = waypoint;
        }

        routeDistance += CardinalDistance(previous, targetPosition);
        return routeDistance > directDistance + maxDetour;
    }

    private static float CardinalDistance(Vector2 start, Vector2 end)
    {
        var delta = Vector2.Abs(end - start);
        return delta.X + delta.Y;
    }
}
