using Content.Shared.Actions;
using Content.Shared.Interaction.Events;
using Content.Shared.Overworld.Components;
using Content.Shared.Overworld.Events;

namespace Content.Client.Overworld;

/// <summary>
/// Клиентская сторона: только отправляет запросы серверу.
/// Никакой игровой логики здесь нет — всё решает сервер.
/// </summary>
public sealed class OverworldTravelSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();
        Logger.GetSawmill("overworld.client");

        SubscribeLocalEvent<WorldMapActivatorComponent, UseInHandEvent>(OnActivatorUseInHand);

        SubscribeLocalEvent<OverworldTokenComponent, ExitOverworldActionEvent>(OnExitAction);
    }

    private void OnActivatorUseInHand(EntityUid uid, WorldMapActivatorComponent comp, UseInHandEvent args)
    {
        if (!comp.RequiresHolding)
            return;

        RaiseNetworkEvent(new EnterOverworldRequestEvent(GetNetEntity(uid)));
    }

    private void OnExitAction(EntityUid uid, OverworldTokenComponent _, ExitOverworldActionEvent args)
    {
        RaiseNetworkEvent(new ExitOverworldRequestEvent());
        args.Handled = true;
    }
}

public sealed partial class ExitOverworldActionEvent : InstantActionEvent { }
