using Content.Client._NC.Trade.Controls;
using Content.Shared._NC.Trade;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;


namespace Content.Client._NC.Trade;


public sealed partial class NcStoreMenu
{
    private static readonly Comparison<ContractClientData> ContractComparison = CompareContracts;
    private readonly List<string> _contractCardOrder = new();
    private readonly Dictionary<string, NcContractCard> _contractCardsById = new();
    private readonly List<string> _staleContractIdsScratch = new();
    private NcContractConfirmWindow? _activeContractConfirmWindow;

    public void PopulateContracts(List<ContractClientData>? list, int skipCost, string skipCurrency, int skipBalance)
    {
        var contractList = ContractList;
        if (contractList == null)
            return;

        if (list == null || list.Count == 0)
        {
            ResetContractsListToEmpty(contractList);
            ApplyTabsVisibility();
            return;
        }

        var ordered = OrderContracts(list);
        if (!TryUpdateContractsInPlace(contractList, ordered, skipCost, skipCurrency, skipBalance))
            RebuildContracts(contractList, ordered, skipCost, skipCurrency, skipBalance);

        ApplyTabsVisibility();
    }

    private static List<ContractClientData> OrderContracts(List<ContractClientData> contracts)
    {
        var ordered = new List<ContractClientData>(contracts);
        ordered.Sort(ContractComparison);
        return ordered;
    }

    private static int CompareContracts(ContractClientData left, ContractClientData right)
    {
        var diff = left.OfferPoolOrder.CompareTo(right.OfferPoolOrder);
        if (diff != 0)
            return diff;

        diff = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        if (diff != 0)
            return diff;

        return string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }

    private bool TryUpdateContractsInPlace(
        Control contractList,
        List<ContractClientData> ordered,
        int skipCost,
        string skipCurrency,
        int skipBalance
    )
    {
        if (contractList.ChildCount != ordered.Count || _contractCardOrder.Count != ordered.Count)
            return false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var contract = ordered[i];
            if (!_contractCardsById.TryGetValue(contract.Id, out var card) ||
                !string.Equals(_contractCardOrder[i], contract.Id, StringComparison.Ordinal) ||
                !ReferenceEquals(contractList.GetChild(i), card))
                return false;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var contract = ordered[i];
            _contractCardsById[contract.Id].UpdateData(contract, skipCost, skipCurrency, skipBalance);
        }

        return true;
    }

    private void RebuildContracts(
        Control contractList,
        List<ContractClientData> ordered,
        int skipCost,
        string skipCurrency,
        int skipBalance
    )
    {
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        contractList.RemoveAllChildren();
        _contractCardOrder.Clear();

        for (var i = 0; i < ordered.Count; i++)
        {
            var contract = ordered[i];
            activeIds.Add(contract.Id);

            if (!_contractCardsById.TryGetValue(contract.Id, out var card))
            {
                card = CreateContractCard(contract, skipCost, skipCurrency, skipBalance);
                _contractCardsById[contract.Id] = card;
            }
            else
                card.UpdateData(contract, skipCost, skipCurrency, skipBalance);

            contractList.AddChild(card);
            _contractCardOrder.Add(contract.Id);
        }

        PruneContractCards(activeIds);
    }

    private NcContractCard CreateContractCard(
        ContractClientData contract,
        int skipCost,
        string skipCurrency,
        int skipBalance
    )
    {
        var card = new NcContractCard(
            contract,
            _proto,
            _sprites,
            skipCost,
            skipCurrency,
            skipBalance,
            SetActiveContractConfirmWindow);
        card.ApplyUiTheme(_uiColors);
        card.OnClaim += id => OnContractClaim?.Invoke(id);
        card.OnTake += id => OnContractTake?.Invoke(id);
        card.OnSkip += id => OnContractSkip?.Invoke(id);
        card.OnRequestPinpointer += id => OnContractRequestPinpointer?.Invoke(id);
        return card;
    }

    private void SetActiveContractConfirmWindow(NcContractConfirmWindow window)
    {
        if (_activeContractConfirmWindow == window)
            return;

        _activeContractConfirmWindow?.Close();
        _activeContractConfirmWindow = window;
        window.OnClose += () =>
        {
            if (_activeContractConfirmWindow == window)
                _activeContractConfirmWindow = null;
        };
    }

    private void CloseActiveContractConfirmWindow()
    {
        var window = _activeContractConfirmWindow;
        _activeContractConfirmWindow = null;
        window?.Close();
    }

    private void ResetContractsListToEmpty(Control contractList)
    {
        CloseActiveContractConfirmWindow();
        contractList.RemoveAllChildren();
        PruneContractCards(Array.Empty<string>());
        _contractCardOrder.Clear();

        contractList.AddChild(
            new Label
            {
                Text = Loc.GetString("nc-store-contracts-empty"),
                HorizontalAlignment = HAlignment.Center,
                Margin = new(0, 8, 0, 0)
            });
    }

    private void PruneContractCards(IEnumerable<string> activeIds)
    {
        var active = activeIds is HashSet<string> set
            ? set
            : new(activeIds, StringComparer.Ordinal);

        _staleContractIdsScratch.Clear();
        foreach (var id in _contractCardsById.Keys)
            if (!active.Contains(id))
                _staleContractIdsScratch.Add(id);

        for (var i = 0; i < _staleContractIdsScratch.Count; i++)
        {
            var id = _staleContractIdsScratch[i];
            if (_contractCardsById.Remove(id, out var card))
            {
                card.CloseConfirmation();
                card.Parent?.RemoveChild(card);
            }
        }
    }
}
