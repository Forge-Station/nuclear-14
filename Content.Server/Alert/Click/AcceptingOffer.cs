using Content.Server.OfferItem;
using Content.Shared._Forge.Alert;
using Content.Shared.OfferItem;

namespace Content.Server.Alert.Click;

/// <summary>
/// Принимает предложенный предмет по нажатию на алерт.
/// </summary>
public sealed class AcceptOfferAlertSystem : EntitySystem
{
    [Dependency] private readonly OfferItemSystem _offerItem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OfferItemComponent, AcceptOfferAlertEvent>(OnAcceptOffer);
    }

    private void OnAcceptOffer(Entity<OfferItemComponent> ent, ref AcceptOfferAlertEvent args)
    {
        _offerItem.Receive(ent.Owner, ent.Comp);
        args.Handled = true;
    }
}
