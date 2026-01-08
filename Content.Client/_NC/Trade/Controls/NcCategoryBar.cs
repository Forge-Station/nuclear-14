using Robust.Client.UserInterface.Controls;


namespace Content.Client._NC.Trade.Controls;


/// <summary>
///     Vertical list of category buttons with toggle selection and hover feedback.
///     Owns button creation/reuse and only exposes the selected category.
/// </summary>
public sealed class NcCategoryBar : BoxContainer
{
    private static readonly Color SelectedColor = new(0xD9, 0xA4, 0x41);
    private static readonly Color IdleColor = new(0x7C, 0x66, 0x24);

    private readonly Dictionary<string, Button> _buttons = new();
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

            btn.Text = _displayName(catId);
            btn.ToolTip = null;
        }

        UpdateVisuals();
    }

    private Button CreateButton(string catId)
    {
        var btn = new Button
        {
            Text = _displayName(catId),
            ToggleMode = true,
            HorizontalExpand = true,
            ModulateSelfOverride = IdleColor
        };

        btn.OnPressed += _ =>
        {
            var next = Selected == catId ? string.Empty : catId;
            SetSelected(next);
        };

        btn.OnMouseEntered += _ =>
        {
            btn.ModulateSelfOverride = btn.Pressed
                ? Brighten(SelectedColor, 1.2f)
                : Brighten(IdleColor, 1.2f);
        };

        btn.OnMouseExited += _ => { btn.ModulateSelfOverride = btn.Pressed ? SelectedColor : IdleColor; };

        return btn;
    }

    private void UpdateVisuals()
    {
        foreach (var (catId, btn) in _buttons)
        {
            var isSelected = catId == Selected;
            if (btn.Pressed != isSelected)
                btn.Pressed = isSelected;

            btn.ModulateSelfOverride = isSelected ? SelectedColor : IdleColor;
        }
    }

    private static Color Brighten(Color c, float f) =>
        new(MathF.Min(c.R * f, 1f), MathF.Min(c.G * f, 1f), MathF.Min(c.B * f, 1f), c.A);
}
