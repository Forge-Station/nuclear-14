using Content.Server.Actions;
using Content.Server.Mind;
using Content.Shared.Overworld;
using Content.Shared.Overworld.Components;
using Content.Shared.Overworld.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Overworld;

public sealed class OverworldSessionSystem : EntitySystem
{
    private static readonly ProtoId<TravelDestinationPrototype> OverworldDestinationProto = "TravelDest_Overworld";
    private static readonly EntProtoId TokenProto = "OverworldToken";
    private static readonly EntProtoId ExitActionProto = "ActionExitOverworld";

    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly TravelDestinationSystem _destination = default!;
    [Dependency] private readonly TravelNodeSystem _travelNodes = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("overworld.session");

        SubscribeLocalEvent<OverworldTokenComponent, ComponentShutdown>(OnTokenShutdown);
        SubscribeLocalEvent<OverworldTokenComponent, OverworldTravelExitAtCoordinatesEvent>(OnTravelExitAtCoordinates);
    }

    public bool TryEnterOverworld(EntityUid body, ProtoId<TravelDestinationPrototype>? linkedDestination = null)
    {
        if (HasComp<OverworldStasisComponent>(body) || HasComp<OverworldTokenComponent>(body))
            return false;

        if (!_mind.TryGetMind(body, out var mindId, out _))
            return false;

        if (!_destination.TryResolve(OverworldDestinationProto, out var spawnCoords))
        {
            _sawmill.Error($"TryEnterOverworld: failed to resolve default overworld destination '{OverworldDestinationProto}'.");
            return false;
        }

        // Если терминал привязан к конкретной точке (например, Yuma),
        // спавним токен рядом с соответствующим узлом на глобалке.
        if (linkedDestination.HasValue &&
            _travelNodes.TryGetNodeCoordsForDestination(linkedDestination.Value, out var linkedCoords))
        {
            spawnCoords = linkedCoords;
            _sawmill.Debug($"TryEnterOverworld: using linked node '{linkedDestination.Value}' at {spawnCoords}.");
        }

        var stasis = AddComp<OverworldStasisComponent>(body);
        stasis.EnteredAt = _timing.CurTime;

        var token = Spawn(TokenProto, spawnCoords);
        var tokenComp = EnsureComp<OverworldTokenComponent>(token);
        tokenComp.OriginalBody = body;
        tokenComp.IsExiting = false;

        stasis.ActiveToken = token;

        EntityUid? actionEntity = null;
        _actions.AddAction(token, ref actionEntity, ExitActionProto);
        tokenComp.ExitActionEntity = actionEntity;

        _mind.TransferTo(mindId, token);

        RaiseLocalEvent(body, new PlayerEnteredOverworldEvent(body, token));
        return true;
    }

    public bool TryExitOverworld(EntityUid token, EntityCoordinates? returnCoords = null, bool traveledToLocation = false)
    {
        if (!TryComp<OverworldTokenComponent>(token, out var tokenComp))
            return false;

        if (tokenComp.IsExiting)
            return false;

        tokenComp.IsExiting = true;

        var body = tokenComp.OriginalBody;
        if (!EntityManager.EntityExists(body))
        {
            QueueDel(token);
            return false;
        }

        if (_mind.TryGetMind(token, out var mindId, out _))
            _mind.TransferTo(mindId, body);

        RemComp<OverworldStasisComponent>(body);

        if (returnCoords.HasValue)
            _transform.SetCoordinates(body, returnCoords.Value);

        RaiseLocalEvent(body, new PlayerExitedOverworldEvent(body, traveledToLocation));
        QueueDel(token);
        return true;
    }

    private void OnTravelExitAtCoordinates(EntityUid token, OverworldTokenComponent comp, OverworldTravelExitAtCoordinatesEvent ev)
    {
        if (ev.Handled)
            return;

        ev.Handled = true;
        TryExitOverworld(token, ev.Coordinates, traveledToLocation: true);
    }

    private void OnTokenShutdown(EntityUid token, OverworldTokenComponent tokenComp, ref ComponentShutdown ev)
    {
        if (tokenComp.IsExiting)
            return;

        var body = tokenComp.OriginalBody;
        if (!EntityManager.EntityExists(body))
            return;

        if (_mind.TryGetMind(token, out var mindId, out var mindComp) && mindComp != null)
            _mind.TransferTo(mindId, body);

        RemComp<OverworldStasisComponent>(body);
    }
}
