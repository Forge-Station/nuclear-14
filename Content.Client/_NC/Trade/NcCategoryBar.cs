using Robust.Client.UserInterface.Controls;

namespace Content.Client._NC.Trade;

/// <summary>
/// Vertical list of category buttons with toggle selection and hover feedback.
/// Owns button creation/reuse and only exposes the selected category.
/// </summary>
public sealed class NcCategoryBar : BoxContainer
{
    private static readonly Color SelectedColor = new(0xD9, 0xA4, 0x41);
    private static readonly Color IdleColor = new(0x7C, 0x66, 0x24);

    private readonly Dictionary<string, Button> _buttons = new();
    private readonly List<string> _ordered = new();

    private Func<string, string> _displayName = static id => id;
    private Func<string, string> _toolTip = static id => id;

    private string _selected = string.Empty;

    public event Action<string>? OnSelectedChanged;

    public string Selected => _selected;

    public NcCategoryBar()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public void Configure(Func<string, string> displayName, Func<string, string> toolTip)
    {
        _displayName = displayName;
        _toolTip = toolTip;
    }

    public void SetCategories(IReadOnlyList<string> categories, string selectedCategory)
    {
        _ordered.Clear();
        for (var i = 0; i < categories.Count; i++)
            _ordered.Add(categories[i]);

        SyncButtons();

        SetSelected(selectedCategory, raiseEvent: false);
    }

    public void SetSelected(string selectedCategory, bool raiseEvent = true)
    {
        if (!string.IsNullOrEmpty(selectedCategory) && !_buttons.ContainsKey(selectedCategory))
            selectedCategory = string.Empty;

        if (_selected == selectedCategory)
            return;

        _selected = selectedCategory;
        UpdateVisuals();

        if (raiseEvent)
            OnSelectedChanged?.Invoke(_selected);
    }

    private void SyncButtons()
    {
        var needed = new HashSet<string>(_ordered);
        var toRemove = new List<string>();
        foreach (var key in _buttons.Keys)
            if (!needed.Contains(key))
                toRemove.Add(key);

        foreach (var key in toRemove)
        {
            var btn = _buttons[key];
            RemoveChild(btn);
            _buttons.Remove(key);
        }

        for (var i = 0; i < _ordered.Count; i++)
        {
            var catId = _ordered[i];
            if (_buttons.ContainsKey(catId))
                continue;

            var btn = CreateButton(catId);
            _buttons.Add(catId, btn);
            AddChild(btn);
        }

        for (var i = 0; i < _ordered.Count; i++)
        {
            var catId = _ordered[i];
            if (!_buttons.TryGetValue(catId, out var btn))
                continue;

            RemoveChild(btn);
            AddChild(btn);
        }

        for (var i = 0; i < _ordered.Count; i++)
        {
            var catId = _ordered[i];
            if (!_buttons.TryGetValue(catId, out var btn))
                continue;

            btn.Text = _displayName(catId);
            btn.ToolTip = _toolTip(catId);
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
            ToolTip = _toolTip(catId),
            ModulateSelfOverride = IdleColor
        };

        btn.OnPressed += _ =>
        {
            var next = _selected == catId ? string.Empty : catId;
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
            var isSelected = catId == _selected;
            if (btn.Pressed != isSelected)
                btn.Pressed = isSelected;

            btn.ModulateSelfOverride = isSelected ? SelectedColor : IdleColor;
        }
    }

    private static Color Brighten(Color c, float f) =>
        new(MathF.Min(c.R * f, 1f), MathF.Min(c.G * f, 1f), MathF.Min(c.B * f, 1f), c.A);
}
