using Content.Client._NC.Trade.Theme;
using Content.Shared._NC.Trade;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private const string ContractAllPoolFilterId = "";
    private const string ContractUncategorizedPoolFilterId = "nc.internal.contract.uncategorized";
    private static readonly Comparison<ContractPoolFilterData> ContractPoolFilterComparison = CompareContractPoolFilters;
    private readonly Dictionary<string, Button> _contractPoolButtonsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ContractPoolFilterData> _contractPoolFiltersById = new(StringComparer.Ordinal);
    private readonly List<ContractPoolFilterData> _contractPoolFilterOrder = new();
    private readonly List<ContractClientData> _orderedContracts = new();
    private readonly List<ContractClientData> _visibleContractsScratch = new();
    private ButtonGroup _contractPoolButtonGroup = new(false);
    private int _contractSkipBalance;
    private int _contractSkipCost;
    private string _contractSkipCurrency = string.Empty;
    private string _selectedContractPoolFilterId = ContractAllPoolFilterId;

    private static int CompareContractPoolFilters(ContractPoolFilterData left, ContractPoolFilterData right)
    {
        var diff = left.Order.CompareTo(right.Order);
        if (diff != 0)
            return diff;

        diff = string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        if (diff != 0)
            return diff;

        return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }

    private void RefreshContractsList()
    {
        var contractList = ContractList;
        if (contractList == null)
            return;

        if (_orderedContracts.Count == 0)
        {
            ResetContractsListToEmpty(contractList);
            return;
        }

        _visibleContractsScratch.Clear();

        for (var i = 0; i < _orderedContracts.Count; i++)
        {
            var contract = _orderedContracts[i];
            if (ShouldShowContractForSelectedPool(contract))
                _visibleContractsScratch.Add(contract);
        }

        if (_visibleContractsScratch.Count == 0)
        {
            ResetContractsListToMessage(contractList, Loc.GetString("nc-store-contracts-category-empty"));
            return;
        }

        if (!TryUpdateContractsInPlace(
                contractList,
                _visibleContractsScratch,
                _contractSkipCost,
                _contractSkipCurrency,
                _contractSkipBalance))
        {
            RebuildContracts(
                contractList,
                _visibleContractsScratch,
                _contractSkipCost,
                _contractSkipCurrency,
                _contractSkipBalance);
        }
    }

    private bool ShouldShowContractForSelectedPool(ContractClientData contract) =>
        string.IsNullOrEmpty(_selectedContractPoolFilterId) ||
        string.Equals(GetContractPoolFilterId(contract), _selectedContractPoolFilterId, StringComparison.Ordinal);

    private void SyncContractPoolFilters(IReadOnlyList<ContractClientData> ordered)
    {
        _contractPoolFiltersById.Clear();
        _contractPoolFilterOrder.Clear();

        for (var i = 0; i < ordered.Count; i++)
            AddContractPoolFilter(ordered[i]);

        _contractPoolFilterOrder.AddRange(_contractPoolFiltersById.Values);
        _contractPoolFilterOrder.Sort(ContractPoolFilterComparison);

        if (_contractPoolFilterOrder.Count <= 1 ||
            (!string.IsNullOrEmpty(_selectedContractPoolFilterId) &&
             !_contractPoolFiltersById.ContainsKey(_selectedContractPoolFilterId)))
        {
            _selectedContractPoolFilterId = ContractAllPoolFilterId;
        }

        RebuildContractPoolFilterButtons(ordered.Count);
    }

    private void AddContractPoolFilter(ContractClientData contract)
    {
        var id = GetContractPoolFilterId(contract);
        if (!_contractPoolFiltersById.TryGetValue(id, out var filter))
        {
            filter = new ContractPoolFilterData
            {
                Id = id,
                Name = GetContractPoolFilterName(contract),
                Order = contract.OfferPoolOrder
            };

            _contractPoolFiltersById.Add(id, filter);
        }

        filter.Count++;

        if (filter.Order == int.MaxValue && contract.OfferPoolOrder != int.MaxValue)
            filter.Order = contract.OfferPoolOrder;
    }

    private void RebuildContractPoolFilterButtons(int totalCount)
    {
        ContractsCategoryRow.RemoveAllChildren();
        _contractPoolButtonsById.Clear();

        var showFilters = _contractPoolFilterOrder.Count > 1;
        ContractsCategoryPanel.Visible = showFilters;

        if (!showFilters)
            return;

        _contractPoolButtonGroup = new(false);
        AddContractPoolFilterButton(
            ContractAllPoolFilterId,
            Loc.GetString("nc-store-contract-category-all"),
            totalCount);

        for (var i = 0; i < _contractPoolFilterOrder.Count; i++)
        {
            var filter = _contractPoolFilterOrder[i];
            AddContractPoolFilterButton(filter.Id, filter.Name, filter.Count);
        }

        UpdateContractPoolFilterButtonStates();
    }

    private void AddContractPoolFilterButton(string id, string name, int count)
    {
        var button = new Button
        {
            Text = Loc.GetString("nc-store-contract-category-button", ("name", name), ("count", count)),
            ToolTip = string.IsNullOrEmpty(id)
                ? Loc.GetString("nc-store-contract-category-all-tooltip")
                : Loc.GetString("nc-store-contract-category-tooltip", ("category", name)),
            ToggleMode = true,
            ClipText = false,
            HorizontalExpand = false,
            VerticalAlignment = VAlignment.Center,
            MinWidth = CalculateContractFilterButtonMinWidth(name, count),
            MinHeight = 30,
            Margin = new(0, 0, 2, 0)
        };

        button.Group = _contractPoolButtonGroup;
        if (string.Equals(id, _selectedContractPoolFilterId, StringComparison.Ordinal) && !button.Pressed)
            button.Pressed = true;

        button.ModulateSelfOverride = Color.White;
        button.OnPressed += _ => SelectContractPoolFilter(id);
        _contractPoolButtonsById.Add(id, button);
        ContractsCategoryRow.AddChild(button);
    }

    private void SelectContractPoolFilter(string id)
    {
        if (string.Equals(_selectedContractPoolFilterId, id, StringComparison.Ordinal))
        {
            UpdateContractPoolFilterButtonStates();
            return;
        }

        _selectedContractPoolFilterId = id;
        CloseActiveContractConfirmWindow();
        UpdateContractPoolFilterButtonStates();
        RefreshContractsList();
    }

    private void UpdateContractPoolFilterButtonStates()
    {
        if (!_contractPoolButtonsById.TryGetValue(_selectedContractPoolFilterId, out var selectedButton))
        {
            _selectedContractPoolFilterId = ContractAllPoolFilterId;
            _contractPoolButtonsById.TryGetValue(_selectedContractPoolFilterId, out selectedButton);
        }

        if (selectedButton != null && !selectedButton.Pressed)
            selectedButton.Pressed = true;

        foreach (var (_, button) in _contractPoolButtonsById)
        {
            var selected = ReferenceEquals(button, selectedButton);
            button.StyleBoxOverride = CreateContractPoolFilterButtonStyle(selected);
            button.ModulateSelfOverride = Color.White;
            button.Label.FontColorOverride = selected
                ? NcStoreUiTheme.ResolveColor(_uiColors.TabFontActive, "#DDF4FF")
                : NcStoreUiTheme.ResolveColor(_uiColors.TabFontInactive, "#AFC5CE");
        }
    }

    private StyleBoxFlat CreateContractPoolFilterButtonStyle(bool selected)
    {
        var panelBackground = NcStoreUiTheme.ResolveColor(_uiColors.CategoriesPanelBackground, "#151B20");
        var activeBackground = NcStoreUiTheme.ResolveColor(_uiColors.TabActiveBackground, "#2C556A");
        var inactiveBackground = NcStoreUiTheme.ResolveColor(_uiColors.TabInactiveBackground, "#253038");
        var baseBackground = selected
            ? activeBackground
            : NcStoreUiTheme.Blend(panelBackground, inactiveBackground, 0.72f);
        var background = selected
            ? NcStoreUiTheme.Blend(baseBackground, Color.White, 0.04f)
            : baseBackground;
        var border = selected
            ? NcStoreUiTheme.ResolveColor(_uiColors.TabActiveBorder, "#8BC7E5")
            : NcStoreUiTheme.WithAlpha(NcStoreUiTheme.ResolveColor(_uiColors.CategoriesDivider, "#567A8C"), 0.60f);

        var style = NcStoreUiTheme.Flat(background, border);
        style.SetContentMarginOverride(StyleBox.Margin.Left, 12);
        style.SetContentMarginOverride(StyleBox.Margin.Right, 12);
        style.SetContentMarginOverride(StyleBox.Margin.Top, 5);
        style.SetContentMarginOverride(StyleBox.Margin.Bottom, 5);
        return style;
    }

    private static float CalculateContractFilterButtonMinWidth(string name, int count) =>
        Math.Clamp(38 + (name.Length + count.ToString().Length + 3) * 8, 96, 360);

    private static string GetContractPoolFilterId(ContractClientData contract) =>
        string.IsNullOrWhiteSpace(contract.OfferPoolId)
            ? ContractUncategorizedPoolFilterId
            : contract.OfferPoolId.Trim();

    private static string GetContractPoolFilterName(ContractClientData contract) =>
        string.IsNullOrWhiteSpace(contract.OfferPoolName)
            ? Loc.GetString("nc-store-category-fallback")
            : contract.OfferPoolName.Trim();

    private sealed class ContractPoolFilterData
    {
        public int Count;
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int Order = int.MaxValue;
    }
}
