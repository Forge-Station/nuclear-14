using System.Linq;
using Content.Shared._NC.Trade;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;


namespace Content.Client._NC.Trade.Controls;


/// <summary>
///     Dedicated listings pane for a single <see cref="StoreMode" />.
///     Owns paging, searching and per-listing UI caching.
/// </summary>
public sealed class NcListingGrid : BoxContainer
{
    private const int PageSize = 96;
    private readonly Dictionary<string, (NcStoreListingControl Ctrl, int Sig)> _cache = new();
    private readonly Dictionary<string, StoreListingData> _itemById = new();

    private readonly StoreMode _mode;
    private readonly IPrototypeManager _proto;
    private readonly Dictionary<string, int> _qtyCache = new();
    private readonly List<StoreListingData> _scratchFiltered = new();

    private readonly List<string> _scratchKeys = new();
    private readonly HashSet<string> _scratchSeenProtos = new();

    private readonly Dictionary<string, string> _searchIndex = new();
    private readonly SpriteSystem _sprites;

    private IReadOnlyList<StoreListingData> _allItems = Array.Empty<StoreListingData>();
    private Func<string, int> _balanceResolver = _ => int.MaxValue;
    private Action<StoreListingData, int> _emit = (_, _) => { };

    private int _page = 1;
    private string _searchLower = string.Empty;
    private string _selectedCategory = string.Empty;

    public NcListingGrid(StoreMode mode, IPrototypeManager proto, SpriteSystem sprites)
    {
        _mode = mode;
        _proto = proto;
        _sprites = sprites;

        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 0;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    public void ResetPaging() => _page = 1;

    public void ClearCaches()
    {
        _searchIndex.Clear();
        _cache.Clear();
        _qtyCache.Clear();
    }

    public void SyncAvailableIds(IReadOnlyCollection<string> ids)
    {
        _scratchKeys.Clear();
        foreach (var key in _qtyCache.Keys)
            if (!ids.Contains(key))
                _scratchKeys.Add(key);

        for (var i = 0; i < _scratchKeys.Count; i++)
            _qtyCache.Remove(_scratchKeys[i]);

        _scratchKeys.Clear();
        foreach (var key in _cache.Keys)
            if (!ids.Contains(key))
                _scratchKeys.Add(key);

        for (var i = 0; i < _scratchKeys.Count; i++)
            _cache.Remove(_scratchKeys[i]);
    }

    public void PrepareSearchIndex(IEnumerable<string> productEntities)
    {
        _searchIndex.Clear();
        _scratchSeenProtos.Clear();

        foreach (var protoId in productEntities)
        {
            if (string.IsNullOrWhiteSpace(protoId))
                continue;

            if (!_scratchSeenProtos.Add(protoId))
                continue;

            AddToSearchIndex(protoId);
        }
    }

    public int Refresh(
        IReadOnlyList<StoreListingData> allItems,
        string? selectedCategory,
        string? searchLower,
        Func<string, int> balanceResolver,
        Action<StoreListingData, int> emit
    )
    {
        _allItems = allItems;
        _selectedCategory = selectedCategory ?? string.Empty;
        _searchLower = (searchLower ?? string.Empty).Trim().ToLowerInvariant();
        _balanceResolver = balanceResolver;
        _emit = emit;

        _itemById.Clear();
        for (var i = 0; i < allItems.Count; i++)
        {
            var it = allItems[i];
            if (it.Mode != _mode)
                continue;
            if (string.IsNullOrWhiteSpace(it.Id))
                continue;
            _itemById[it.Id] = it;
        }

        return RefreshInternal();
    }


    public void UpdateDynamicOnly(Func<string, int> balanceResolver)
    {
        _balanceResolver = balanceResolver;

        foreach (var child in Children)
        {
            if (child is not NcStoreListingControl ctrl)
                continue;

            if (!_itemById.TryGetValue(ctrl.ListingId, out var it))
                continue;

            var balanceHint = _mode == StoreMode.Buy
                ? _balanceResolver(it.CurrencyId)
                : int.MaxValue;

            ctrl.UpdateDynamicData(balanceHint, it.Remaining, it.Owned);
        }
    }

    private int RefreshInternal()
    {
        var hasCat = !string.IsNullOrEmpty(_selectedCategory);
        var hasSearch = !string.IsNullOrWhiteSpace(_searchLower);

        _scratchFiltered.Clear();

        if (hasCat || hasSearch)
        {
            foreach (var it in _allItems)
            {
                if (it.Mode != _mode)
                    continue;

                if (hasCat && it.Category != _selectedCategory)
                    continue;

                if (hasSearch && !MatchesSearch(it.ProductEntity))
                    continue;

                _scratchFiltered.Add(it);
            }
        }

        if (!hasCat && !hasSearch)
        {
            Children.Clear();
            AddChild(new Label { Text = Loc.GetString("nc-store-select-category"), });
            return 0;
        }

        if (_scratchFiltered.Count == 0)
        {
            Children.Clear();
            var message = Loc.GetString(hasCat ? "nc-store-empty-category-search" : "nc-store-empty-search");
            AddChild(new Label { Text = message, });
            return 0;
        }

        if (_page < 1)
            _page = 1;

        var total = _scratchFiltered.Count;
        var take = Math.Min(total, PageSize * _page);

        Children.Clear();
        AddListingRange(_scratchFiltered, 0, take);
        AddOrUpdateMoreButton(total, take);

        return total;
    }

    private void AddListingRange(List<StoreListingData> source, int startInclusive, int endExclusive)
    {
        for (var i = startInclusive; i < endExclusive; i++)
        {
            var it = source[i];

            var balanceHint = _mode == StoreMode.Buy ? _balanceResolver(it.CurrencyId) : int.MaxValue;
            var sig = Sig(it);

            if (!_cache.TryGetValue(it.Id, out var tuple) || tuple.Sig != sig)
            {
                var initQty = _qtyCache.GetValueOrDefault(it.Id, 1);
                var actionsEnabled = true;

                var created = new NcStoreListingControl(it, _sprites, balanceHint, initQty, actionsEnabled);
                created.OnQtyChanged = newQty => _qtyCache[it.Id] = newQty;

                _cache[it.Id] = (created, sig);
                tuple = (created, sig);
            }

            var ctrl = tuple.Ctrl;
            ctrl.OnBuyPressed = _mode == StoreMode.Buy ? qty => _emit(it, qty) : null;
            ctrl.OnSellPressed = _mode == StoreMode.Sell ? qty => _emit(it, qty) : null;
            ctrl.UpdateDynamicData(balanceHint, it.Remaining, it.Owned);

            AddChild(ctrl);

            if (i < endExclusive - 1)
            {
                AddChild(new PanelContainer
                {
                    MinSize = new Vector2i(0, 1),
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = Color.FromHex("#A0A0A0")
                    }
                });
            }
        }
    }

    private void AddOrUpdateMoreButton(int totalCount, int shown)
    {
        if (shown >= totalCount)
            return;

        var left = totalCount - shown;

        var more = new Button
        {
            Name = "MoreButton",
            Text = Loc.GetString("nc-store-show-more", ("count", left)),
            HorizontalExpand = true,
            Margin = new(0, 8, 0, 8)
        };

        more.OnPressed += _ =>
        {
            _page++;
            RefreshInternal();
        };

        AddChild(more);
    }

    private static int Sig(StoreListingData d) =>
        HashCode.Combine(d.Id, d.ProductEntity, d.Category, d.Price, d.CurrencyId, d.Mode);

    private void AddToSearchIndex(string protoId)
    {
        if (_searchIndex.ContainsKey(protoId))
            return;

        if (!_proto.TryIndex<EntityPrototype>(protoId, out var p))
            return;

        _searchIndex[protoId] = (p.Name + "\n" + p.Description).ToLowerInvariant();
    }

    private bool MatchesSearch(string protoId)
    {
        if (string.IsNullOrWhiteSpace(protoId))
            return false;

        if (string.IsNullOrWhiteSpace(_searchLower))
            return true;

        if (!_searchIndex.TryGetValue(protoId, out var hay))
        {
            AddToSearchIndex(protoId);
            _searchIndex.TryGetValue(protoId, out hay);
        }

        return hay != null && hay.Contains(_searchLower, StringComparison.Ordinal);
    }
}
