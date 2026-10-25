using System.Numerics;
using System.Linq;
using Content.Shared._Forge.Newspapers;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._Forge.Newspapers;

/// <summary>A fixed text area with restricted inline emphasis and consistent measurement.</summary>
public sealed class NewspaperTextBlock : Control
{
    private Vector2 _bounds;
    private int _fontSize;
    private NewspaperFont _fontKind;
    private NewspaperFont _cachedKind;
    private int _cachedSize;
    private Font[]? _fonts;
    private NewspaperTypography.StyledLayout? _layout;
    private (string Text, int Width, int Height, NewspaperFont Font, int Size, bool Markup)? _layoutKey;
    private bool _editable;
    private string _displayText = "";
    private string _placeholder = "";
    private NewspaperInk _ink;
    private bool _showPlaceholder;
    public float DisplayScale { get; set; } = 1f;
    public bool MarkupEnabled { get; set; }
    public bool Fits { get; private set; } = true;
    public string Text => _displayText;
    public event Action? OnEditRequested;
    public Vector2 BlockSize { get => _bounds; set => SetBounds((int)value.X, (int)value.Y); }
    public int FontSize { get => _fontSize; set => _fontSize = value; }
    public bool Centered { get; set; }
    public string DisplayText { set => SetContent(value); }
    public string PlaceholderText { set => _placeholder = value; }
    public NewspaperTextBlock() : this(100, 30, 16) { }
    public NewspaperTextBlock(int width, int height, int maxFontSize, string placeholder = "", bool centered = false)
    {
        _bounds = new Vector2(width, height); _fontSize = maxFontSize; _placeholder = placeholder; Centered = centered;
        SetSize = MinSize = MaxSize = _bounds;
        RectClipContent = true;
    }
    public void SetFont(NewspaperFont font) => _fontKind = font;
    public void LoadText(string text) => _displayText = text;
    public void SetBounds(int width, int height)
    {
        _bounds = new Vector2(width, height);
        SetSize = MinSize = MaxSize = _bounds * DisplayScale;
        InvalidateMeasure();
    }
    internal static string FontPath(NewspaperFont kind, bool bold, bool italic)
    {
        bold |= kind is NewspaperFont.SansBold or NewspaperFont.SerifBold;
        if (kind is NewspaperFont.Sans or NewspaperFont.SansBold)
            return $"/Fonts/NotoSans/NotoSans-{(bold ? italic ? "BoldItalic" : "Bold" : italic ? "Italic" : "Regular")}.ttf";
        if (kind == NewspaperFont.Cambria && !bold && !italic) return "/Fonts/Cambria.ttf";
        var weight = bold ? "Bold" : kind == NewspaperFont.SerifSemibold ? "Semibold" : "Regular";
        return $"/Fonts/_Forge/SourceSerif4/SourceSerif4-{(italic ? weight == "Regular" ? "It" : weight + "It" : weight)}.ttf";
    }
    public void SetContent(string text, NewspaperInk ink = NewspaperInk.Black)
    {
        _displayText = text; _ink = ink;
        _showPlaceholder = _editable && string.IsNullOrWhiteSpace(text);
        // Editor prompts are affordances, not article content. Keep them small and uniform.
        var renderedKind = _showPlaceholder ? NewspaperFont.Sans : _fontKind;
        var renderedSize = _showPlaceholder ? 14 : _fontSize;
        if (_fonts == null || _cachedKind != renderedKind || _cachedSize != renderedSize)
        {
            var cache = IoCManager.Resolve<IResourceCache>();
            _fonts = Enumerable.Range(0, 4).Select(style => (Font)new VectorFont(
                cache.GetResource<FontResource>(FontPath(renderedKind, (style & 1) != 0, (style & 2) != 0)), renderedSize)).ToArray();
            _cachedKind = renderedKind; _cachedSize = renderedSize;
        }
        var content = _showPlaceholder ? _placeholder : text;
        var key = (content, (int)_bounds.X, (int)_bounds.Y, renderedKind, renderedSize, MarkupEnabled && !_showPlaceholder);
        if (_layoutKey == key) return;
        _layoutKey = key;
        _layout = NewspaperTypography.LayoutStyled(content,
            (bold, italic) => _fonts[(bold ? 1 : 0) | (italic ? 2 : 0)], (int)_bounds.X, (int)_bounds.Y, MarkupEnabled && !_showPlaceholder);
        Fits = _showPlaceholder || _layout.Fits;
    }
    public void SetEditable(bool editable)
    {
        if (_editable == editable) return;
        _editable = editable;
        MouseFilter = editable ? MouseFilterMode.Stop : MouseFilterMode.Ignore;
        SetContent(_displayText, _ink);
    }
    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_layout == null || _fonts == null) return;
        var scale = UIScale * DisplayScale;
        var color = _showPlaceholder ? Color.FromHex("#747A7B") : InkColor(_ink);
        var y = 0f;
        foreach (var line in _layout.Lines)
        {
            var x = Centered ? Math.Max(0, (_bounds.X - line.Width) / 2) : 0;
            foreach (var character in line.Characters)
            {
                var font = _fonts[(character.Bold ? 1 : 0) | (character.Italic ? 2 : 0)];
                font.DrawChar(handle, character.Rune, new Vector2(x, y + line.Ascent) * scale, scale, color);
                x += font.GetCharMetrics(character.Rune, 1)?.Advance ?? 0;
            }
            y += line.LineHeight;
        }
    }
    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (!_editable || args.Function != EngineKeyFunctions.UIClick) return;
        OnEditRequested?.Invoke(); args.Handle();
    }
    protected override Vector2 MeasureOverride(Vector2 availableSize) => _bounds * DisplayScale;
    internal static Color InkColor(NewspaperInk ink) => ink switch
    {
        NewspaperInk.Red => new Color(132, 48, 42), NewspaperInk.Blue => new Color(47, 68, 110),
        NewspaperInk.Green => new Color(49, 93, 70), _ => new Color(33, 36, 38),
    };
}
