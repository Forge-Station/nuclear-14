using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._NC.Trade;

public sealed partial class NcStoreMenu
{
    private void CaptureTabsIfNeeded()
    {
        if (_tabsCaptured)
            return;

        _tabsCaptured = true;

        _tabBuy = TabBuy;
        _tabSell = TabSell;
        _tabContracts = TabContracts;
    }

    private void EnsureTab(Control? tab, bool shouldExist)
    {
        if (Tabs == null || tab == null)
            return;

        var exists = Tabs.Children.Contains(tab);

        if (shouldExist && !exists)
            Tabs.AddChild(tab);
        else if (!shouldExist && exists)
            Tabs.RemoveChild(tab);
    }

    private void ApplyTabsVisibility()
    {
        if (Tabs == null)
            return;

        CaptureTabsIfNeeded();

        EnsureTab(_tabBuy, _hasBuyTab);
        EnsureTab(_tabSell, _hasSellTab);
        EnsureTab(_tabContracts, _hasContractsTab);

        var count = Tabs.ChildCount;
        if (count <= 0)
            return;

        var curIndex = Tabs.CurrentTab;
        if (curIndex < 0 || curIndex >= count)
            Tabs.CurrentTab = 0;
    }

    private void UpdateHeaderVisibility()
    {
        var showStoreHeader = _hasBuyTab || _hasSellTab;

        HeaderFrame.Visible = showStoreHeader;
        Header.Visible = showStoreHeader;
    }
}
