using System.Linq;
using Content.Client._NC.Trade.Theme;
using Content.Client.Stylesheets;
using Content.Shared._NC.Trade;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private void ApplyUiTheme(StoreUiColorsData? uiColors)
    {
        var resolved = uiColors ?? new StoreUiColorsData();
        var themeHash = ComputeUiThemeHash(resolved);
        if (themeHash == _uiThemeHash && AreUiColorsEqual(_uiColors, resolved))
            return;

        _uiThemeHash = themeHash;
        _uiColors = resolved;
        ApplyBaseTheme();
        ApplyLocalTabStyle();
    }

    private void ApplyBaseTheme()
    {
        var windowChromeText = NcStoreUiTheme.ResolveColor(_uiColors.TabFontActive, "#E4D3AF");
        if (NcStoreUiTheme.IsTooDark(windowChromeText))
            windowChromeText = NcStoreUiTheme.ResolveColor(_uiColors.HeaderBalanceText, "#E4D3AF");
        if (NcStoreUiTheme.IsTooDark(windowChromeText))
            windowChromeText = Color.FromHex("#E4D3AF");

        ApplyWindowBackdropTheme(
            NcStoreUiTheme.ResolveColor(_uiColors.TabsShellBackground, "#1A1115"),
            NcStoreUiTheme.ResolveColor(_uiColors.TabsShellBorder, "#6B2833"));

        WindowTitle.FontColorOverride = windowChromeText;
        WindowTitle.ModulateSelfOverride = Color.White;

        var headerFrameStyle = new StyleBoxFlat
        {
            BackgroundColor = NcStoreUiTheme.ResolveColor(_uiColors.HeaderBackground, "#222830"),
            BorderColor = NcStoreUiTheme.ResolveColor(_uiColors.HeaderBorder, "#4C4438"),
            BorderThickness = new(0, 0, 0, 1)
        };
        headerFrameStyle.SetContentMarginOverride(StyleBox.Margin.Left, 0);
        headerFrameStyle.SetContentMarginOverride(StyleBox.Margin.Top, 0);
        headerFrameStyle.SetContentMarginOverride(StyleBox.Margin.Right, 0);
        headerFrameStyle.SetContentMarginOverride(StyleBox.Margin.Bottom, 0);
        HeaderFrame.PanelOverride = headerFrameStyle;

        Header.ApplyUiTheme(_uiColors);

        var tabsShellStyle = NcStoreUiTheme.Fill(
            NcStoreUiTheme.ResolveColor(_uiColors.TabsShellBackground, "#171A20"));
        tabsShellStyle.SetContentMarginOverride(StyleBox.Margin.Horizontal, 0);
        tabsShellStyle.SetContentMarginOverride(StyleBox.Margin.Vertical, 0);
        TabsShell.PanelOverride = tabsShellStyle;

        var tabsFrameStyle = NcStoreUiTheme.Fill(NcStoreUiTheme.ResolveColor(_uiColors.TabsFrameBackground, "#1B1F26"));
        tabsFrameStyle.SetContentMarginOverride(StyleBox.Margin.Horizontal, 1);
        tabsFrameStyle.SetContentMarginOverride(StyleBox.Margin.Vertical, 1);
        TabsFrame.PanelOverride = tabsFrameStyle;

        var tabContentColor = NcStoreUiTheme.ResolveColor(_uiColors.TabContentBackground, "#1B1F26");
        var tabContentBorder = NcStoreUiTheme.ResolveColor(_uiColors.TabsFrameBorder, "#40382C");

        TabBuy.PanelOverride = CreateTabContentStyle(tabContentColor, tabContentBorder);
        TabSell.PanelOverride = CreateTabContentStyle(tabContentColor, tabContentBorder);
        TabBarter.PanelOverride = CreateTabContentStyle(tabContentColor, tabContentBorder);
        TabContracts.PanelOverride = CreateTabContentStyle(tabContentColor, tabContentBorder);

        BuyView.ApplyUiTheme(_uiColors);
        SellView.ApplyUiTheme(_uiColors);
        BarterView.ApplyUiTheme(_uiColors);
    }

    private void ApplyLocalTabStyle()
    {
        var tabFont = IoCManager.Resolve<IResourceCache>().NotoStack("Bold", 16, true);

        var active = new StyleBoxFlat
        {
            BackgroundColor = NcStoreUiTheme.ResolveColor(_uiColors.TabActiveBackground, "#3A3123"),
            BorderColor = NcStoreUiTheme.ResolveColor(_uiColors.TabActiveBorder, "#A9833E"),
            BorderThickness = new(1)
        };
        active.SetContentMarginOverride(StyleBox.Margin.Left, 14);
        active.SetContentMarginOverride(StyleBox.Margin.Right, 14);
        active.SetContentMarginOverride(StyleBox.Margin.Top, 7);
        active.SetContentMarginOverride(StyleBox.Margin.Bottom, 7);

        var inactive = new StyleBoxFlat
        {
            BackgroundColor = NcStoreUiTheme.ResolveColor(_uiColors.TabInactiveBackground, "#232830"),
            BorderColor = NcStoreUiTheme.ResolveColor(_uiColors.TabInactiveBorder, "#444C58"),
            BorderThickness = new(1)
        };
        inactive.SetContentMarginOverride(StyleBox.Margin.Left, 14);
        inactive.SetContentMarginOverride(StyleBox.Margin.Right, 14);
        inactive.SetContentMarginOverride(StyleBox.Margin.Top, 7);
        inactive.SetContentMarginOverride(StyleBox.Margin.Bottom, 7);

        var tabsBarStyle = new StyleBoxFlat
        {
            BackgroundColor = NcStoreUiTheme.ResolveColor(_uiColors.TabsBarBackground, "#171B21"),
            BorderColor = NcStoreUiTheme.ResolveColor(_uiColors.TabsBarBorder, "#3E4652"),
            BorderThickness = new(0, 0, 0, 1)
        };
        tabsBarStyle.SetContentMarginOverride(StyleBox.Margin.Left, 1);
        tabsBarStyle.SetContentMarginOverride(StyleBox.Margin.Right, 1);
        tabsBarStyle.SetContentMarginOverride(StyleBox.Margin.Top, 1);
        tabsBarStyle.SetContentMarginOverride(StyleBox.Margin.Bottom, 1);

        Tabs.PanelStyleBoxOverride = tabsBarStyle;

        var activeFont = NcStoreUiTheme.ResolveColor(_uiColors.TabFontActive, "#E6D6B2");
        var inactiveFont = NcStoreUiTheme.ResolveColor(_uiColors.TabFontInactive, "#AFA690");

        Tabs.TabFontColorOverride = activeFont;
        Tabs.TabFontColorInactiveOverride = inactiveFont;

        var baseRules = IoCManager.Resolve<IUserInterfaceManager>().Stylesheet?.Rules ?? Array.Empty<StyleRule>();

        Tabs.StyleIdentifier = "nc-store-tabs";
        Tabs.Stylesheet = new(
            baseRules
                .Concat(
                    new[]
                    {
                        new StyleRule(
                            new SelectorElement(typeof(TabContainer), null, "nc-store-tabs", null),
                            new[]
                            {
                                new StyleProperty(TabContainer.StylePropertyPanelStyleBox, tabsBarStyle),
                                new StyleProperty(TabContainer.StylePropertyTabStyleBox, active),
                                new StyleProperty(TabContainer.StylePropertyTabStyleBoxInactive, inactive),
                                new StyleProperty(TabContainer.stylePropertyTabFontColor, activeFont),
                                new StyleProperty(TabContainer.StylePropertyTabFontColorInactive, inactiveFont),
                                new StyleProperty("font", tabFont)
                            })
                    })
                .ToArray());

        Tabs.ForceRunStyleUpdate();
    }

    private static StyleBoxFlat CreateTabContentStyle(Color background, Color border)
    {
        var style = NcStoreUiTheme.Flat(background, border);
        style.BorderThickness = new(1);
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 0);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 0);
        return style;
    }

    private void ApplyWindowBackdropTheme(Color background, Color border)
    {
        _windowBackdropPanel ??= ResolveWindowBackdropPanel();
        if (_windowBackdropPanel == null)
            return;

        var shellStyle = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new(1)
        };
        shellStyle.SetContentMarginOverride(StyleBox.Margin.Left, 0);
        shellStyle.SetContentMarginOverride(StyleBox.Margin.Top, 0);
        shellStyle.SetContentMarginOverride(StyleBox.Margin.Right, 0);
        shellStyle.SetContentMarginOverride(StyleBox.Margin.Bottom, 0);

        _windowBackdropPanel.PanelOverride = shellStyle;
        _windowBackdropPanel.ModulateSelfOverride = Color.White;
    }

    private PanelContainer? ResolveWindowBackdropPanel()
    {
        PanelContainer? firstPanel = null;

        for (var i = 0; i < ChildCount; i++)
        {
            if (GetChild(i) is not PanelContainer panel)
                continue;

            firstPanel ??= panel;

            if (panel.HasStyleClass(StyleBase.ClassAngleRect))
                return panel;
        }

        return firstPanel;
    }

    private static ulong ComputeUiThemeHash(StoreUiColorsData colors)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;

            hash = MixStableHash(hash, colors.TabsShellBackground);
            hash = MixStableHash(hash, colors.TabsShellBorder);
            hash = MixStableHash(hash, colors.TabsFrameBackground);
            hash = MixStableHash(hash, colors.TabsFrameBorder);
            hash = MixStableHash(hash, colors.TabContentBackground);
            hash = MixStableHash(hash, colors.TabsBarBackground);
            hash = MixStableHash(hash, colors.TabsBarBorder);
            hash = MixStableHash(hash, colors.TabActiveBackground);
            hash = MixStableHash(hash, colors.TabActiveBorder);
            hash = MixStableHash(hash, colors.TabInactiveBackground);
            hash = MixStableHash(hash, colors.TabInactiveBorder);
            hash = MixStableHash(hash, colors.TabFontActive);
            hash = MixStableHash(hash, colors.TabFontInactive);
            hash = MixStableHash(hash, colors.CategoriesPanelBackground);
            hash = MixStableHash(hash, colors.CategoriesDivider);
            hash = MixStableHash(hash, colors.CategoryButtonIdle);
            hash = MixStableHash(hash, colors.CategoryButtonSelected);
            hash = MixStableHash(hash, colors.HeaderBackground);
            hash = MixStableHash(hash, colors.HeaderBorder);
            hash = MixStableHash(hash, colors.HeaderBalanceText);
            hash = MixStableHash(hash, colors.SearchBoxBackground);
            hash = MixStableHash(hash, colors.SearchBoxBorder);
            hash = MixStableHash(hash, colors.SearchIconColor);
            hash = MixStableHash(hash, colors.ListingCardBackground);
            hash = MixStableHash(hash, colors.ListingCardBorder);
            hash = MixStableHash(hash, colors.ListingDivider);
            hash = MixStableHash(hash, colors.ListingTitleColor);

            return hash;
        }
    }

    private static ulong MixStableHash(ulong hash, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return unchecked((hash ^ 0U) * 1099511628211UL);

        return unchecked((hash ^ (uint) StableStringHash(value)) * 1099511628211UL);
    }

    private static int StableStringHash(string value)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            var hash = unchecked((int) 2166136261u);

            for (var i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * fnvPrime;

            return hash;
        }
    }

    private static bool AreUiColorsEqual(StoreUiColorsData left, StoreUiColorsData right) =>
        string.Equals(left.TabsShellBackground, right.TabsShellBackground, StringComparison.Ordinal) &&
        string.Equals(left.TabsShellBorder, right.TabsShellBorder, StringComparison.Ordinal) &&
        string.Equals(left.TabsFrameBackground, right.TabsFrameBackground, StringComparison.Ordinal) &&
        string.Equals(left.TabsFrameBorder, right.TabsFrameBorder, StringComparison.Ordinal) &&
        string.Equals(left.TabContentBackground, right.TabContentBackground, StringComparison.Ordinal) &&
        string.Equals(left.TabsBarBackground, right.TabsBarBackground, StringComparison.Ordinal) &&
        string.Equals(left.TabsBarBorder, right.TabsBarBorder, StringComparison.Ordinal) &&
        string.Equals(left.TabActiveBackground, right.TabActiveBackground, StringComparison.Ordinal) &&
        string.Equals(left.TabActiveBorder, right.TabActiveBorder, StringComparison.Ordinal) &&
        string.Equals(left.TabInactiveBackground, right.TabInactiveBackground, StringComparison.Ordinal) &&
        string.Equals(left.TabInactiveBorder, right.TabInactiveBorder, StringComparison.Ordinal) &&
        string.Equals(left.TabFontActive, right.TabFontActive, StringComparison.Ordinal) &&
        string.Equals(left.TabFontInactive, right.TabFontInactive, StringComparison.Ordinal) &&
        string.Equals(left.CategoriesPanelBackground, right.CategoriesPanelBackground, StringComparison.Ordinal) &&
        string.Equals(left.CategoriesDivider, right.CategoriesDivider, StringComparison.Ordinal) &&
        string.Equals(left.CategoryButtonIdle, right.CategoryButtonIdle, StringComparison.Ordinal) &&
        string.Equals(left.CategoryButtonSelected, right.CategoryButtonSelected, StringComparison.Ordinal) &&
        string.Equals(left.HeaderBackground, right.HeaderBackground, StringComparison.Ordinal) &&
        string.Equals(left.HeaderBorder, right.HeaderBorder, StringComparison.Ordinal) &&
        string.Equals(left.HeaderBalanceText, right.HeaderBalanceText, StringComparison.Ordinal) &&
        string.Equals(left.SearchBoxBackground, right.SearchBoxBackground, StringComparison.Ordinal) &&
        string.Equals(left.SearchBoxBorder, right.SearchBoxBorder, StringComparison.Ordinal) &&
        string.Equals(left.SearchIconColor, right.SearchIconColor, StringComparison.Ordinal) &&
        string.Equals(left.ListingCardBackground, right.ListingCardBackground, StringComparison.Ordinal) &&
        string.Equals(left.ListingCardBorder, right.ListingCardBorder, StringComparison.Ordinal) &&
        string.Equals(left.ListingDivider, right.ListingDivider, StringComparison.Ordinal) &&
        string.Equals(left.ListingTitleColor, right.ListingTitleColor, StringComparison.Ordinal);
}
