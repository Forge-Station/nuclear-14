using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Overworld;
using Content.Shared.Overworld.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.Overworld;

/// <summary>
/// Серверный локальный эвент: узел глобалки резолвнул целевые координаты
/// и просит сессию вывести игрока из overworld-токена в эти координаты.
/// </summary>
public sealed class OverworldTravelExitAtCoordinatesEvent : EntityEventArgs
{
    public EntityCoordinates Coordinates;
    public bool Handled;

    public OverworldTravelExitAtCoordinatesEvent(EntityCoordinates coordinates)
    {
        Coordinates = coordinates;
    }
}

public sealed class TravelNodeSystem : EntitySystem
{
    [Dependency] private readonly TravelDestinationSystem _destination = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("overworld.travelnode");

        SubscribeLocalEvent<TravelNodeComponent, ActivateInWorldEvent>(OnTravelNodeInteract);
    }

    private void OnTravelNodeInteract(EntityUid uid, TravelNodeComponent comp, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!HasComp<OverworldTokenComponent>(args.User))
            return;

        if (!_destination.TryResolve(comp.Destination, out var destCoords))
        {
            _sawmill.Warning($"OnTravelNodeInteract: failed to resolve destination '{comp.Destination}'.");
            return;
        }

        args.Handled = true;

        // Отдельная сессионная система уже выполнит безопасный выход + перенос тела.
        var ev = new OverworldTravelExitAtCoordinatesEvent(destCoords);
        RaiseLocalEvent(args.User, ev);
    }

    /// <summary>
    /// Находит координаты travel-node на глобалке по его назначению (Destination).
    /// Используется при входе в overworld, чтобы спавнить токен у "привязанного" узла.
    /// </summary>
    public bool TryGetNodeCoordsForDestination(ProtoId<TravelDestinationPrototype> destination, out EntityCoordinates coords)
    {
        coords = default;

        var query = EntityQueryEnumerator<TravelNodeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var node, out var xform))
        {
            if (node.Destination != destination)
                continue;

            coords = xform.Coordinates;
            return true;
        }

        return false;
    }
}
