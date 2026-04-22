using Content.Client.Message;
using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Timing;


namespace Content.Client._NC.Trade.Controls;


public sealed class NcStoreHeaderBar : BoxContainer
{
    private const int DefaultSearchDebounceMs = 120;
    private const int CurrencyGroupSpacing = 14;
    private const int CurrencyIconSize = 24;

    private sealed class CurrencyGroup
    {
        public readonly BoxContainer Container;
        public readonly TextureRect Icon;
        public readonly RichTextLabel Amount;

        public CurrencyGroup()
        {
            Icon = new TextureRect
            {
                MinSize = new(CurrencyIconSize, CurrencyIconSize),
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                Margin = new(0, 0, 4, 0),
                VerticalAlignment = VAlignment.Center
            };

            Amount = new RichTextLabel
            {
                HorizontalExpand = false,
                VerticalAlignment = VAlignment.Center
            };
            Amount.AddStyleClass("LabelHeading");

            Container = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = false,
                VerticalAlignment = VAlignment.Center,
                Margin = new(0, 0, CurrencyGroupSpacing, 0)
            };
            Container.AddChild(Icon);
            Container.AddChild(Amount);
        }
    }

    private readonly BoxContainer _balancesRow;
    private readonly Label _emptyBalanceLabel;
    private readonly List<CurrencyGroup> _balanceGroups = new();
    private int _activeGroupCount;

    private readonly Dictionary<string, Texture> _currencyIconCache = new();
    private readonly LineEdit _searchBar;
    private readonly Label _searchIcon;
    private readonly List<(string Currency, int Amount)> _lastBalances = new();
    private IPrototypeManager? _proto;

    private int _searchToken;
    private SpriteSystem? _sprites;
    private Color _balanceTextColor = Color.FromHex("#FFFF00");

    public NcStoreHeaderBar()
    {
        Orientation = LayoutOrientation.Horizontal;

        _balancesRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = false,
            VerticalAlignment = VAlignment.Center
        };

        _emptyBalanceLabel = new Label
        {
            VerticalAlignment = VAlignment.Center
        };
        _emptyBalanceLabel.AddStyleClass("LabelHeading");

        _searchBar = new()
        {
            HorizontalExpand = false,
            MinWidth = 250,
            Access = AccessLevel.Public
        };

        _searchIcon = new Label
        {
            Text = "🔍",
            Margin = new(0, 0, 4, 0),
            VerticalAlignment = VAlignment.Center,
        };

        AddChild(_balancesRow);
        AddChild(_emptyBalanceLabel);
        AddChild(new() { HorizontalExpand = true, });
        AddChild(_searchIcon);
        AddChild(_searchBar);

        ApplyUiTheme(new StoreUiColorsData());
        ShowEmptyBalance();

        _searchBar.OnTextChanged += _ => HandleSearchTextChanged();
    }

    public event Action<string>? OnSearchChanged;

    /// <summary>
    ///     Optional: bind services for currency icon resolution.
    ///     Call once from NcStoreMenu after it resolved its dependencies.
    /// </summary>
    public void BindServices(IPrototypeManager proto, SpriteSystem sprites)
    {
        _proto = proto;
        _sprites = sprites;
        _currencyIconCache.Clear();
    }

    public void SetSearchText(string text) => _searchBar.Text = text;

    public string GetSearchText() => _searchBar.Text;

    public void SetBalances(IReadOnlyDictionary<string, int> balancesByCurrency)
    {
        _lastBalances.Clear();

        if (balancesByCurrency.Count == 0)
        {
            ShowEmptyBalance();
            return;
        }

        // Collect non-empty entries in insertion order (server iterates CurrencyWhitelist in order
        // and writes into BalancesByCurrency, so dictionary iteration gives the intended display order).
        var ordered = new List<(string Currency, int Amount)>(balancesByCurrency.Count);
        foreach (var (cur, amt) in balancesByCurrency)
        {
            if (string.IsNullOrWhiteSpace(cur))
                continue;
            ordered.Add((cur, amt));
        }

        _lastBalances.AddRange(ordered);

        if (ordered.Count == 0)
        {
            ShowEmptyBalance();
            return;
        }

        _emptyBalanceLabel.Visible = false;
        ShowBalanceGroups(ordered);
    }

    private void ShowEmptyBalance()
    {
        HideAllBalanceGroups();
        _emptyBalanceLabel.Visible = true;
        _emptyBalanceLabel.Text = "0";
    }

    private void HideAllBalanceGroups()
    {
        for (var i = 0; i < _activeGroupCount; i++)
            _balanceGroups[i].Container.Visible = false;

        _activeGroupCount = 0;
    }

    private void ShowBalanceGroups(List<(string Currency, int Amount)> ordered)
    {
        EnsureBalanceGroupCount(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var (cur, amt) = ordered[i];
            var group = _balanceGroups[i];
            group.Container.Visible = true;
            // Remove trailing margin on the last group so the balances row ends tidily.
            group.Container.Margin = i == ordered.Count - 1
                ? new(0, 0, 0, 0)
                : new(0, 0, CurrencyGroupSpacing, 0);
            group.Amount.SetMarkup($"[font size=14][color={ColorToHex(_balanceTextColor)}]{amt}[/color][/font]");
            SetCurrencyIconFor(group.Icon, cur);
        }

        // Hide groups from a previous, larger balance set.
        for (var i = ordered.Count; i < _activeGroupCount; i++)
            _balanceGroups[i].Container.Visible = false;

        _activeGroupCount = ordered.Count;
    }

    public void ApplyUiTheme(StoreUiColorsData colors)
    {
        _balanceTextColor = ThemeColor(colors.HeaderBalanceText, "#FFFF00");
        _emptyBalanceLabel.ModulateSelfOverride = _balanceTextColor;

        _searchBar.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = ThemeColor(colors.SearchBoxBackground, "#2B2E35"),
            BorderColor = ThemeColor(colors.SearchBoxBorder, "#4C4438"),
            BorderThickness = new(1)
        };
        _searchIcon.ModulateSelfOverride = ThemeColor(colors.SearchIconColor, "#FFFFFF");

        if (_lastBalances.Count == 0)
        {
            ShowEmptyBalance();
        }
        else
        {
            ShowBalanceGroups(_lastBalances);
        }
    }

    private void EnsureBalanceGroupCount(int count)
    {
        while (_balanceGroups.Count < count)
        {
            var group = new CurrencyGroup();
            _balanceGroups.Add(group);
            _balancesRow.AddChild(group.Container);
        }
    }

    private void SetCurrencyIconFor(TextureRect target, string? currencyId)
    {
        if (string.IsNullOrWhiteSpace(currencyId) || _proto == null || _sprites == null)
        {
            target.Texture = null;
            return;
        }

        if (_currencyIconCache.TryGetValue(currencyId, out var cached))
        {
            target.Texture = cached;
            return;
        }

        if (_proto.TryIndex<StackPrototype>(currencyId, out var stackProto) &&
            _proto.TryIndex<EntityPrototype>(stackProto.Spawn, out var entProto))
        {
            var tex = _sprites.GetPrototypeIcon(entProto).Default;
            _currencyIconCache[currencyId] = tex;
            target.Texture = tex;
            return;
        }

        target.Texture = null;
    }

    private void HandleSearchTextChanged()
    {
        var token = ++_searchToken;
        var text = _searchBar.Text.Trim();

        Timer.Spawn(
            TimeSpan.FromMilliseconds(DefaultSearchDebounceMs),
            () =>
            {
                if (token != _searchToken)
                    return;

                OnSearchChanged?.Invoke(text);
            });
    }

    private static Color ThemeColor(string? value, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                return Color.FromHex(value);
            }
            catch
            {
                // Keep defaults if YAML contains invalid colors.
            }
        }

        return Color.FromHex(fallback);
    }

    private static string ColorToHex(Color color)
    {
        var r = (byte) Math.Clamp((int) MathF.Round(color.R * 255f), 0, 255);
        var g = (byte) Math.Clamp((int) MathF.Round(color.G * 255f), 0, 255);
        var b = (byte) Math.Clamp((int) MathF.Round(color.B * 255f), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
