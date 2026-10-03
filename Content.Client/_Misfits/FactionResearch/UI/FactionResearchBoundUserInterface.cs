using Content.Shared._Misfits.FactionResearch;
using Robust.Client.UserInterface;

namespace Content.Client._Misfits.FactionResearch.UI;

public sealed class FactionResearchBoundUserInterface : BoundUserInterface
{
    private FactionResearchMenu? _menu;

    public FactionResearchBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<FactionResearchMenu>();
        _menu.OnPrint += id => SendMessage(new FactionResearchPrintMessage(id));
        _menu.OnConvert += ent => SendMessage(new FactionResearchConvertMessage(ent));
        _menu.OnWithdrawDisk += amount => SendMessage(new FactionResearchWithdrawDiskMessage(amount));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is FactionResearchBoundInterfaceState msg)
            _menu?.UpdateState(msg);
    }
}
