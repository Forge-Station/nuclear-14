using Robust.Client.UserInterface.Controls;


namespace Content.Client._NC.Trade.Controls;


public sealed class NcCategoryBar : BoxContainer
{
    private readonly Dictionary<string, NcCategoryButtonControl> _buttons = new();
    private readonly List<string> _ordered = new();
    private readonly HashSet<string> _scratchNeeded = new();
    private readonly List<string> _scratchRemove = new();

    private Func<string, string> _displayName = static id => id;

    private Func<string, string> _toolTip = static id => id;

    public NcCategoryBar()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public string Selected { get; private set; } = string.Empty;

    public event Action<string>? OnSelectedChanged;

    public void Configure(Func<string, string> displayName, Func<string, string> toolTip)
    {
        _displayName = displayName;
        _toolTip = toolTip;
    }

    public void SetCategories(IReadOnlyList<string> categories, string selectedCategory)
    {
        _ordered.Clear();
        foreach (var t in categories)
            _ordered.Add(t);

        SyncButtons();
        SetSelected(selectedCategory, false);
    }

    public void SetSelected(string selectedCategory, bool raiseEvent = true)
    {
        if (!string.IsNullOrEmpty(selectedCategory) && !_buttons.ContainsKey(selectedCategory))
            selectedCategory = string.Empty;

        if (Selected == selectedCategory)
            return;

        Selected = selectedCategory;
        UpdateVisuals();

        if (raiseEvent)
            OnSelectedChanged?.Invoke(Selected);
    }

    private void SyncButtons()
    {
        _scratchNeeded.Clear();
        foreach (var t in _ordered)
            _scratchNeeded.Add(t);

        _scratchRemove.Clear();
        foreach (var key in _buttons.Keys)
            if (!_scratchNeeded.Contains(key))
                _scratchRemove.Add(key);

        foreach (var key in _scratchRemove)
        {
            var btn = _buttons[key];
            RemoveChild(btn);
            _buttons.Remove(key);
        }

        foreach (var catId in _ordered)
        {
            if (_buttons.ContainsKey(catId))
                continue;

            var btn = CreateButton(catId);
            _buttons.Add(catId, btn);
            AddChild(btn);
        }

        foreach (var catId in _ordered)
        {
            if (!_buttons.TryGetValue(catId, out var btn))
                continue;

            RemoveChild(btn);
            AddChild(btn);
        }

        foreach (var catId in _ordered)
        {
            if (!_buttons.TryGetValue(catId, out var btn))
                continue;

            var tip = _toolTip(catId);
            btn.Bind(catId, _displayName(catId), string.IsNullOrWhiteSpace(tip) ? null : tip);
        }

        UpdateVisuals();
    }

    private NcCategoryButtonControl CreateButton(string catId)
    {
        var btn = new NcCategoryButtonControl();
        var tip = _toolTip(catId);
            btn.Bind(catId, _displayName(catId), string.IsNullOrWhiteSpace(tip) ? null : tip);

        btn.OnCategoryPressed += id =>
        {
            var next = Selected == id ? string.Empty : id;
            SetSelected(next);
        };

        return btn;
    }

    private void UpdateVisuals()
    {
        foreach (var (catId, btn) in _buttons)
        {
            var isSelected = catId == Selected;
            btn.SetSelected(isSelected);
        }
    }
}
