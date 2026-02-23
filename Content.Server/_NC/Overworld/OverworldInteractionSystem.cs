using Content.Server.Mind;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Overworld.Components;
using Content.Shared.Overworld.Events;

namespace Content.Server.Overworld;

public sealed class OverworldInteractionSystem : EntitySystem
{
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly OverworldSessionSystem _session = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("overworld.interaction");

        SubscribeNetworkEvent<EnterOverworldRequestEvent>(OnEnterRequest);
        SubscribeNetworkEvent<ExitOverworldRequestEvent>(OnExitRequest);

        SubscribeLocalEvent<WorldMapActivatorComponent, UseInHandEvent>(OnActivatorUseInHand);
        SubscribeLocalEvent<WorldMapActivatorComponent, InteractHandEvent>(OnActivatorInteractHand);

        SubscribeLocalEvent<OverworldTokenComponent, ExitOverworldActionEvent>(OnExitAction);
    }

    private void OnEnterRequest(EnterOverworldRequestEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (!_mind.TryGetMind(session, out _, out var mindComp))
            return;

        if (mindComp.CurrentEntity is not { } body)
            return;

        if (HasComp<OverworldTokenComponent>(body))
            return;

        if (!TryGetEntity(msg.Activator, out var activatorUid) ||
            !TryComp<WorldMapActivatorComponent>(activatorUid, out var activatorComp))
        {
            _sawmill.Warning($"OnEnterRequest: invalid activator from '{session.Name}'.");
            return;
        }

        _session.TryEnterOverworld(body, activatorComp.LinkedDestination);
    }

    private void OnExitRequest(ExitOverworldRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!_mind.TryGetMind(args.SenderSession, out _, out var mindComp))
            return;

        if (mindComp.CurrentEntity is not { } current)
            return;

        _session.TryExitOverworld(current);
    }

    private void OnActivatorUseInHand(EntityUid uid, WorldMapActivatorComponent comp, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        if (_session.TryEnterOverworld(args.User, comp.LinkedDestination))
            args.Handled = true;
    }

    private void OnActivatorInteractHand(EntityUid uid, WorldMapActivatorComponent comp, ref InteractHandEvent args)
    {
        if (args.Handled || comp.RequiresHolding)
            return;

        if (_session.TryEnterOverworld(args.User, comp.LinkedDestination))
            args.Handled = true;
    }

    private void OnExitAction(EntityUid token, OverworldTokenComponent comp, ref ExitOverworldActionEvent args)
    {
        if (args.Handled)
            return;

        if (_session.TryExitOverworld(token))
            args.Handled = true;
    }
}
