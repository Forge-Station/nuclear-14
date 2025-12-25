using System.Linq;
using Content.Client.Stylesheets;
using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;


namespace Content.Client._NC.Trade;


public sealed class NcStoreListingControl : PanelContainer
{
    private const int SlotPx = 96;
    private const int PriceW = 96;
    private const int PriceH = 32;
    private const int TextMax = 420;
    private const int QtyMaxDigits = 6;
    private const int MaxTotalDisplay = 999_999;
    private const int DescMaxChars = 220;
    private readonly bool _actionsEnabled;
    private readonly Button? _minusBtn;
    private readonly Label? _ownedLbl;
    private readonly Button? _plusBtn;
    private readonly LineEdit _qtyEdit;
    private readonly Label? _qtyLbl;
    private readonly Label? _remainingLbl;


    private readonly IUserInterfaceManager _ui = IoCManager.Resolve<IUserInterfaceManager>();
    private int _lastBalanceHint = int.MaxValue;
    private int _maxQty;
    private Button? _priceButton;
    private Label? _priceLbl;
    private int _qty;
    private StoreListingData _staticData;
    private bool _suppressQtyEditChange;

    public NcStoreListingControl(
        StoreListingData data,
        SpriteSystem sprites,
        int balanceHint = int.MaxValue,
        int initialQty = 1,
        bool actionsEnabled = true
    )
    {
        _staticData = data;
        _actionsEnabled = actionsEnabled;
        _lastBalanceHint = balanceHint;

        Margin = new(6, 6, 6, 6);
        HorizontalExpand = true;

        var card = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new(0.08f, 0.08f, 0.09f, 0.9f),
                BorderColor = Color.FromHex("#B08D3B"),
                BorderThickness = new(1),
                PaddingLeft = 10,
                PaddingRight = 10,
                PaddingTop = 8,
                PaddingBottom = 8
            }
        };
        AddChild(card);

        var mainCol = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true
        };
        card.AddChild(mainCol);

        var pm = IoCManager.Resolve<IPrototypeManager>();
        pm.TryIndex<EntityPrototype>(data.ProductEntity, out var proto);

        var title = new Label
        {
            Text = proto?.Name ?? data.ProductEntity,
            HorizontalExpand = true,
            ClipText = true,
            ToolTip = proto?.Name ?? data.ProductEntity,
            Margin = new(2, 0, 2, 4)
        };
        title.StyleClasses.Add(StyleBase.StyleClassLabelHeading);
        mainCol.AddChild(title);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true
        };
        mainCol.AddChild(row);

        var leftCol = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = false,
            MinSize = new Vector2i(SlotPx, 0)
        };

        if (MakeSlot(proto, sprites) is { } slot)
            leftCol.AddChild(slot);

        row.AddChild(leftCol);
        var textCol = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            HorizontalExpand = true,
            VerticalExpand = false
        };

        var desc = MakeDescription(proto);
        desc.Margin = new(0, 0, 6, 0);
        textCol.AddChild(desc);
        row.AddChild(textCol);

        var actionCol = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = false,
            MinSize = new Vector2i(PriceW, PriceH)
        };

        var remainingCap = data.Remaining >= 0 ? data.Remaining : int.MaxValue;
        var ownedCap = data.Mode == StoreMode.Sell ? data.Owned : int.MaxValue;
        var moneyCap = data.Mode == StoreMode.Buy && data.Price > 0
            ? balanceHint / data.Price
            : int.MaxValue;

        _maxQty = Math.Min(remainingCap, Math.Min(ownedCap, moneyCap));
        _qty = Math.Clamp(initialQty, MinAllowed, Math.Max(MinAllowed, _maxQty));

        var qtyRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            HorizontalExpand = false
        };

        var minusBtn = new Button
        {
            Text = "−",
            MinSize = new Vector2i(24, 24)
        };
        _minusBtn = minusBtn;

        var qtyLbl = new Label
        {
            Text = _qty.ToString(),
            MinSize = new Vector2i(28, 24),
            HorizontalAlignment = HAlignment.Center
        };
        _qtyLbl = qtyLbl;

        var qtyEdit = new LineEdit
        {
            Text = _qty.ToString(),
            MinSize = new Vector2i(40, 24),
            HorizontalExpand = false
        };
        _qtyEdit = qtyEdit;

        var plusBtn = new Button
        {
            Text = "+",
            MinSize = new Vector2i(24, 24)
        };
        _plusBtn = plusBtn;

        var noQty = _maxQty <= 0 || !_actionsEnabled;
        minusBtn.Disabled = noQty;
        plusBtn.Disabled = noQty;
        _qtyEdit.Editable = !noQty;

        if (!_actionsEnabled)
        {
            minusBtn.ToolTip = Loc.GetString("nc-store-only-mass-sell");
            plusBtn.ToolTip = Loc.GetString("nc-store-only-mass-sell");
            _qtyEdit.ToolTip = Loc.GetString("nc-store-only-mass-sell");
        }

        minusBtn.OnPressed += _ =>
        {
            if (!_actionsEnabled)
                return;

            if (_qty > MinAllowed)
                SetQty(_qty - 1, data, qtyLbl);
        };

        plusBtn.OnPressed += _ =>
        {
            if (!_actionsEnabled)
                return;

            if (_qty < _maxQty)
                SetQty(_qty + 1, data, qtyLbl);
        };

        _qtyEdit.OnTextChanged += _ =>
        {
            if (!_actionsEnabled)
                return;

            if (_suppressQtyEditChange)
                return;

            var digits = new string(_qtyEdit.Text.Where(char.IsDigit).Take(QtyMaxDigits).ToArray());
            if (digits.Length == 0)
            {
                _suppressQtyEditChange = true;
                _qtyEdit.Text = _qty.ToString();
                _qtyEdit.CursorPosition = _qtyEdit.Text.Length;
                _suppressQtyEditChange = false;
                return;
            }

            if (!int.TryParse(digits, out var v))
                v = _qty;

            var clamped = Math.Clamp(v, MinAllowed, Math.Max(MinAllowed, _maxQty));
            var newText = clamped.ToString();

            if (_qtyEdit.Text != newText)
            {
                _suppressQtyEditChange = true;
                _qtyEdit.Text = newText;
                _qtyEdit.CursorPosition = _qtyEdit.Text.Length;
                _suppressQtyEditChange = false;
            }

            SetQty(clamped, data, qtyLbl);
        };

        _qtyEdit.OnTextEntered += _ =>
        {
            if (!_actionsEnabled)
                return;

            if (_maxQty <= 0 || _qty <= 0)
                return;

            switch (data.Mode)
            {
                case StoreMode.Buy:
                    OnBuyPressed?.Invoke(_qty);
                    break;
                case StoreMode.Sell:
                    OnSellPressed?.Invoke(_qty);
                    break;
            }
        };

        qtyRow.AddChild(minusBtn);
        qtyRow.AddChild(qtyLbl);
        qtyRow.AddChild(qtyEdit);
        qtyRow.AddChild(plusBtn);
        actionCol.AddChild(qtyRow);

        if (data.Remaining != 0)
        {
            actionCol.AddChild(MakePriceButton(data, sprites));
            UpdateTotal(data);
        }
        else
        {
            actionCol.AddChild(
                new Label
                {
                    Text = data.Mode == StoreMode.Buy
                        ? Loc.GetString("nc-store-no-stock")
                        : Loc.GetString("nc-store-buying-finished"),
                    HorizontalAlignment = HAlignment.Center,
                    Modulate = Color.FromHex("#C0C0C0"),
                    Margin = new(0, 8, 0, 0)
                });
        }

        var showRemaining = data.Remaining >= 0;
        var showOwned = data.Owned > 0;

        if (showRemaining)
        {
            _remainingLbl = new()
            {
                Text = data.Mode == StoreMode.Buy
                    ? Loc.GetString("nc-store-remaining", ("count", data.Remaining))
                    : Loc.GetString("nc-store-will-buy", ("count", data.Remaining)),
                HorizontalAlignment = HAlignment.Center,
                Modulate = Color.FromHex("#C0C0C0"),
                Margin = new(0, 2, 0, 0)
            };
            actionCol.AddChild(_remainingLbl);
        }

        if (showOwned)
        {
            _ownedLbl = new()
            {
                Text = Loc.GetString("nc-store-owned", ("count", data.Owned)),
                HorizontalAlignment = HAlignment.Center,
                Modulate = Color.FromHex("#C0C0C0"),
                Margin = new(0, 2, 0, 0)
            };
            actionCol.AddChild(_ownedLbl);
        }

        row.AddChild(actionCol);
    }

    private int MinAllowed => _maxQty <= 0 ? 0 : 1;

    public Action<int>? OnBuyPressed { get; set; }
    public Action<int>? OnSellPressed { get; set; }
    public Action<int>? OnQtyChanged { get; set; }


    public string ListingId => _staticData.Id;

    public void UpdateIdentity(StoreListingData newData) => _staticData = newData;

    public void UpdateDynamicData(int playerBalance, int remainingStock, int playerOwned)
    {
        _lastBalanceHint = playerBalance;

        var remainingCap = remainingStock >= 0 ? remainingStock : int.MaxValue;
        var ownedCap = _staticData.Mode == StoreMode.Sell ? playerOwned : int.MaxValue;

        var moneyCap = _staticData.Mode == StoreMode.Buy && _staticData.Price > 0
            ? playerBalance / _staticData.Price
            : int.MaxValue;

        _maxQty = Math.Min(remainingCap, Math.Min(ownedCap, moneyCap));

        var minAllowed = _maxQty <= 0 ? 0 : 1;
        var clamped = Math.Clamp(_qty, minAllowed, Math.Max(minAllowed, _maxQty));


        if (clamped != _qty)
        {
            _qty = clamped;
            if (_qtyLbl != null)
                _qtyLbl.Text = _qty.ToString();

            if (_qtyEdit.Text != _qty.ToString())
            {
                if (_ui.KeyboardFocused != _qtyEdit)
                {
                    _suppressQtyEditChange = true;
                    _qtyEdit.Text = _qty.ToString();
                    _qtyEdit.CursorPosition = _qtyEdit.Text.Length;
                    _suppressQtyEditChange = false;
                }
            }

            OnQtyChanged?.Invoke(_qty);
        }


        var noQty = _maxQty <= 0 || !_actionsEnabled;
        if (_minusBtn != null)
            _minusBtn.Disabled = noQty || _qty <= minAllowed;
        if (_plusBtn != null)
            _plusBtn.Disabled = noQty || _qty >= _maxQty;

        _qtyEdit.Editable = !noQty;

        if (_priceButton != null)
        {
            var disable =
                !_actionsEnabled ||
                noQty ||
                remainingStock == 0 ||
                _staticData.Mode == StoreMode.Sell && playerOwned <= 0;

            _priceButton.Disabled = disable;
        }

        if (_remainingLbl != null)
        {
            if (remainingStock >= 0)
            {
                _remainingLbl.Text = _staticData.Mode == StoreMode.Buy
                    ? Loc.GetString("nc-store-remaining", ("count", remainingStock))
                    : Loc.GetString("nc-store-will-buy", ("count", remainingStock));
            }
        }

        if (_ownedLbl != null)
        {
            _ownedLbl.Visible = playerOwned > 0;
            if (playerOwned > 0)
                _ownedLbl.Text = Loc.GetString("nc-store-owned", ("count", playerOwned));
        }

        UpdateTotal(_staticData);
    }


    private static Texture? TryGetCurrencyIcon(string currencyId, SpriteSystem sprites)
    {
        var pm = IoCManager.Resolve<IPrototypeManager>();

        if (!pm.TryIndex<StackPrototype>(currencyId, out var stack))
            return null;

        if (!pm.TryIndex<EntityPrototype>(stack.Spawn, out var ent))
            return null;

        return sprites.GetPrototypeIcon(ent.ID).Default;
    }


    private static Control? MakeSlot(EntityPrototype? proto, SpriteSystem sprites)
    {
        if (proto == null)
            return null;

        if (sprites.GetPrototypeIcon(proto.ID).Default is not { } tex)
            return null;

        var slot = new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassInventorySlotBackground, },
            MinSize = new Vector2i(SlotPx, SlotPx)
        };

        slot.AddChild(
            new TextureRect
            {
                Texture = tex,
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                Margin = new(2)
            });

        return slot;
    }

    private static Control MakeDescription(EntityPrototype? proto)
    {
        var full = proto?.Description ?? string.Empty;
        if (string.IsNullOrWhiteSpace(full))
            return new Label { Text = string.Empty, };

        var trimmed = TrimToChars(full, DescMaxChars);

        var msg = new FormattedMessage();
        msg.AddText(trimmed);

        var rtl = new RichTextLabel
        {
            HorizontalExpand = false,
            VerticalExpand = false,
            MaxWidth = TextMax,
            ToolTip = full
        };
        rtl.SetMessage(msg);
        return rtl;
    }

    private static string TrimToChars(string text, int max)
    {
        if (max <= 0 || string.IsNullOrEmpty(text) || text.Length <= max)
            return text;

        var cut = Math.Max(0, max - 1);
        var span = text.AsSpan(0, cut);
        var lastSpace = span.LastIndexOf(' ');
        var end = lastSpace > 0 ? lastSpace : cut;

        return text.Substring(0, end) + "…";
    }

    private Control MakePriceButton(StoreListingData data, SpriteSystem sprites)
    {
        var btn = new Button
        {
            Text = string.Empty,
            MinSize = new Vector2i(PriceW, PriceH),
            MaxSize = new Vector2i(PriceW, PriceH),
            ClipText = true,
            Margin = new(8, 0, 0, 0),
            StyleClasses = { StyleNano.StyleClassButtonBig, },
            Disabled = !_actionsEnabled
                || data.Remaining == 0
                || data.Mode == StoreMode.Sell && data.Owned <= 0
                || _maxQty <= 0
        };

        _priceButton = btn;

        if (!_actionsEnabled)
            btn.ToolTip = Loc.GetString("nc-store-only-mass-sell");

        var inner = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        if (!string.IsNullOrEmpty(data.CurrencyId))
        {
            if (TryGetCurrencyIcon(data.CurrencyId, sprites) is { } tex)
            {
                inner.AddChild(
                    new TextureRect
                    {
                        Texture = tex,
                        Stretch = TextureRect.StretchMode.KeepAspectCentered,
                        MinSize = new Vector2i(PriceH - 6, PriceH - 6),
                        MaxSize = new Vector2i(PriceH - 6, PriceH - 6),
                        Margin = new(2, 2, 0, 2)
                    });
            }
        }

        _priceLbl = new()
        {
            Text = data.Price.ToString(),
            HorizontalExpand = true,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center
        };
        inner.AddChild(_priceLbl);

        btn.AddChild(inner);

        btn.OnPressed += _ =>
        {
            if (!_actionsEnabled)
                return;

            if (_maxQty <= 0 || _qty <= 0)
                return;

            switch (data.Mode)
            {
                case StoreMode.Buy:
                    OnBuyPressed?.Invoke(_qty);
                    break;
                case StoreMode.Sell:
                    OnSellPressed?.Invoke(_qty);
                    break;
            }
        };

        return btn;
    }


    private void SetQty(int v, StoreListingData data, Label qtyLbl)
    {
        var newQty = Math.Clamp(v, MinAllowed, Math.Max(MinAllowed, _maxQty));
        _qtyEdit.Modulate = Color.White;

        if (newQty == _qty && _qtyEdit.Text == newQty.ToString())
            return;

        _qty = newQty;
        qtyLbl.Text = _qty.ToString();
        var text = _qty.ToString();

        if (_qtyEdit.Text != text)
        {
            _suppressQtyEditChange = true;
            _qtyEdit.Text = text;
            _qtyEdit.CursorPosition = _qtyEdit.Text.Length;
            _suppressQtyEditChange = false;
        }

        var noQty = _maxQty <= 0 || !_actionsEnabled;
        if (_minusBtn != null)
            _minusBtn.Disabled = noQty || _qty <= MinAllowed;
        if (_plusBtn != null)
            _plusBtn.Disabled = noQty || _qty >= _maxQty;
        UpdateTotal(data);
        OnQtyChanged?.Invoke(_qty);
    }

    private void UpdateTotal(StoreListingData data)
    {
        if (_priceLbl is null)
            return;

        var value = _qty <= 0 ? data.Price : (long) data.Price * _qty;
        _priceLbl.Text = value > MaxTotalDisplay ? $"{MaxTotalDisplay}+" : value.ToString();
    }
}
