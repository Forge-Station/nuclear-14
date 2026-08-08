using Content.Client._Forge.Warfront.FactionShop.UI;
using Content.Shared._Forge.Warfront.FactionShop;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Warfront.FactionShop;

[UsedImplicitly]
public sealed class FactionShopBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FactionShopWindow? _window;

    public FactionShopBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindowCenteredLeft<FactionShopWindow>();
        _window.BuyRequested += listing => SendMessage(new FactionShopBuyListingMessage(listing));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not FactionShopBoundUserInterfaceState bState)
            return;

        _window?.UpdateState(bState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _window?.Close();
            _window = null;
        }
    }
}
