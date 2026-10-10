using System.Numerics;
using Content.Client._Forge.Paper;
using Content.Shared._Forge.Paper;
using Content.Shared._Forge.Newspapers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Newspapers;

/// <summary>One renderer for both player layouts and starter prototypes.</summary>
public sealed class NewspaperPage : Control
{
    internal static readonly Color PaperColor = Color.FromHex("#E6E7E5");
    public PaperSurfaceAppearance? SurfaceAppearance { get; set; }
    private readonly PaperSurfaceRenderer _surface = new();
    private NewspaperEdition? _edition;
    private Control? _ruleOverlay;
    private Texture? _currentPhoto;
    private Func<int, Texture?>? _photoResolver;
    private float _previewScale = 1f;
    /// <summary>Display zoom only; saved layout and font sizes are unchanged.</summary>
    public float PreviewScale
    {
        get => _previewScale;
        set
        {
            var scale = Math.Clamp(value, 0.1f, 1.25f);
            if (Math.Abs(scale - _previewScale) < 0.002f) return;
            _previewScale = scale;
            if (_edition != null) SetContent(_edition, _currentPhoto, _photoResolver);
        }
    }
    private readonly List<BlockControl> _blocks = new();
    public int Page { get; set; }
    public bool LayoutMode { get; set; }
    public bool Editable { get; set; }
    public int Selected { get; set; } = -1;
    public bool Snap { get; set; } = true;
    public int OverflowBlock => _blocks.FindIndex(block => !block.Fits);
    public bool ContentFits => _blocks.TrueForAll(block => block.Fits);
    public event Action<int>? OnSelected;
    public event Action? OnChanged;

    public NewspaperPage() { RectClipContent = true; MouseFilter = MouseFilterMode.Pass; }

    public void SetContent(NewspaperEdition edition, Texture? photo = null, Func<int, Texture?>? photoResolver = null)
    {
        if (!ReferenceEquals(_edition, edition) || _blocks.Count != edition.Blocks.Count)
        {
            DisposeAllChildren();
            _blocks.Clear();
            _edition = edition;
            for (var i = 0; i < edition.Blocks.Count; i++)
            {
                var control = new BlockControl(this, edition.Blocks[i], i);
                _blocks.Add(control);
                AddChild(control);
            }
            _ruleOverlay = new RuleOverlay(this);
            AddChild(_ruleOverlay);
        }
        _currentPhoto = photo;
        _photoResolver = photoResolver;
        if (SurfaceAppearance != null) _surface.Update(SurfaceAppearance, edition.Width, edition.Height, Page);
        SetSize = MinSize = MaxSize = new Vector2(edition.Width, edition.Height) * PreviewScale;
        if (_ruleOverlay != null)
            _ruleOverlay.SetSize = _ruleOverlay.MinSize = _ruleOverlay.MaxSize = SetSize;
        for (var i = 0; i < _blocks.Count; i++)
            _blocks[i].Update(edition.Blocks[i].Page != Page ? null :
                photoResolver == null ? photo : photoResolver(edition.PhotoFor(edition.Blocks[i])));
    }

    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (SurfaceAppearance != null && _edition != null && _surface.NeedsUpdate)
            _surface.Update(SurfaceAppearance, _edition.Width, _edition.Height, Page);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (SurfaceAppearance != null) _surface.DrawBackground(handle, PixelSizeBox);
        else handle.DrawRect(PixelSizeBox, PaperColor);
        if (Editable && LayoutMode)
        {
            for (var x = 0f; x < Width; x += 10 * PreviewScale)
                handle.DrawLine(new Vector2(x * UIScale, 0), new Vector2(x * UIScale, PixelHeight), Color.FromHex("#CDD0CE"));
            for (var y = 0f; y < Height; y += 10 * PreviewScale)
                handle.DrawLine(new Vector2(0, y * UIScale), new Vector2(PixelWidth, y * UIScale), Color.FromHex("#CDD0CE"));
        }
    }

    private void DrawRules(DrawingHandleScreen handle)
    {
        // Thin controls can round down to zero pixels at small UI scales and be culled.
        // Draw rules on the page itself, with a minimum thickness of one physical pixel.
        if (_edition != null)
            foreach (var block in _edition.Blocks)
            {
                if (block.Page != Page) continue;
                var color = NewspaperTextBlock.InkColor(block.Ink);
                if (block.Kind == NewspaperBlockKind.Line)
                    handle.DrawRect(RuleRectangle(block, UIScale * PreviewScale), color);
                if (block.Borders == NewspaperBorders.None) continue;
                if (block.Kind == NewspaperBlockKind.Photo && _currentPhoto != null)
                    color = new Color(color.R, color.G, color.B, 0.25f);
                foreach (var edge in new[] { NewspaperBorders.Top, NewspaperBorders.Bottom, NewspaperBorders.Left, NewspaperBorders.Right })
                {
                    if ((block.Borders & edge) == 0) continue;
                    var rule = new NewspaperBlock { X = block.X, Y = block.Y, Width = block.Width, Height = block.Height };
                    if (edge is NewspaperBorders.Top or NewspaperBorders.Bottom)
                    {
                        rule.Height = 1;
                        if (edge == NewspaperBorders.Bottom) rule.Y += block.Height - 1;
                    }
                    else
                    {
                        rule.Width = 1;
                        if (edge == NewspaperBorders.Right) rule.X += block.Width - 1;
                    }
                    handle.DrawRect(RuleRectangle(rule, UIScale * PreviewScale), color);
                }
            }
    }

    internal static UIBox2 RuleRectangle(NewspaperBlock block, float scale)
    {
        var x = MathF.Floor(block.X * scale);
        var y = MathF.Floor(block.Y * scale);
        return new UIBox2(x, y, Math.Max(x + 1, MathF.Ceiling((block.X + block.Width) * scale)),
            Math.Max(y + 1, MathF.Ceiling((block.Y + block.Height) * scale)));
    }

    // Draw after photographs and text so a photo cannot cover its printed frame.
    private sealed class RuleOverlay : Control
    {
        private readonly NewspaperPage _page;
        public RuleOverlay(NewspaperPage page)
        {
            _page = page;
            MouseFilter = MouseFilterMode.Ignore;
        }
        protected override void Draw(DrawingHandleScreen handle)
        {
            _page.DrawRules(handle);
            if (_page.SurfaceAppearance != null) _page._surface.DrawOverlay(handle, PixelSizeBox);
            _page.DrawSelection(handle);
        }
    }

    // Draw on top of every text/photo/rule. A selected photo must not cover its own outline.
    private void DrawSelection(DrawingHandleScreen handle)
    {
        if (!Editable || _edition == null || Selected < 0 || Selected >= _edition.Blocks.Count) return;
        var block = _edition.Blocks[Selected];
        if (block.Page != Page) return;
        var box = RuleRectangle(block, UIScale * PreviewScale);
        var color = Color.FromHex("#51B9F2");
        var thickness = Math.Min(3f, Math.Max(1f, Math.Min(box.Width, box.Height) / 4));
        handle.DrawRect(box, Color.FromHex("#51B9F212"));
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + thickness), color);
        handle.DrawRect(new UIBox2(box.Left, box.Bottom - thickness, box.Right, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + thickness, box.Bottom), color);
        handle.DrawRect(new UIBox2(box.Right - thickness, box.Top, box.Right, box.Bottom), color);
        if (LayoutMode)
        {
            var size = Math.Min(10, Math.Min(box.Width, box.Height));
            handle.DrawRect(new UIBox2(box.Right - size, box.Bottom - size, box.Right, box.Bottom), color);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _surface.Dispose();
        base.Dispose(disposing);
    }

    private sealed class BlockControl : Control
    {
        private readonly NewspaperPage _page;
        private readonly NewspaperBlock _block;
        private readonly int _index;
        private readonly NewspaperTextBlock? _text;
        private readonly TextureRect? _photo;
        private ShaderInstance? _photoShader;
        private readonly NewspaperTextBlock? _placeholder;
        private bool _dragging;
        private bool _resizing;
        private Vector2 _startPointer;
        private Vector2 _startPosition;
        private Vector2 _startSize;
        public bool Fits => _text?.Fits ?? true;

        public BlockControl(NewspaperPage page, NewspaperBlock block, int index)
        {
            _page = page; _block = block; _index = index;
            HorizontalAlignment = HAlignment.Left;
            VerticalAlignment = VAlignment.Top;
            RectClipContent = true;
            if (block.Kind is NewspaperBlockKind.Text or NewspaperBlockKind.EditionNumber or NewspaperBlockKind.PublicationName or NewspaperBlockKind.PageNumber)
            {
                _text = new NewspaperTextBlock();
                AddChild(_text);
                _text.LoadText(block.Text);
                _text.OnEditRequested += () =>
                {
                    _page.Selected = _index;
                    _page.OnSelected?.Invoke(_index);
                };
            }
            else if (block.Kind == NewspaperBlockKind.Photo)
            {
                _photo = new TextureRect { CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered, MouseFilter = MouseFilterMode.Ignore };
                AddChild(_photo);
                _placeholder = new NewspaperTextBlock { MouseFilter = MouseFilterMode.Ignore };
                AddChild(_placeholder);
            }
        }

        public void Update(Texture? photo)
        {
            Visible = _block.Page == _page.Page;
            SetSize = MinSize = MaxSize = new Vector2(_block.Width, _block.Height) * _page.PreviewScale;
            Margin = new Thickness(_block.X * _page.PreviewScale, _block.Y * _page.PreviewScale, 0, 0);
            MouseFilter = _page.Editable && (_page.LayoutMode || _block.Kind == NewspaperBlockKind.Photo) ? MouseFilterMode.Stop : MouseFilterMode.Ignore;
            if (_text != null)
            {
                _text.MarkupEnabled = _block.Kind == NewspaperBlockKind.Text;
                _text.DisplayScale = _page.PreviewScale;
                _text.BlockSize = new Vector2(_block.Width, _block.Height);
                _text.FontSize = _block.FontSize;
                _text.Centered = _block.Centered;
                _text.SetFont(_block.Font);
                _text.PlaceholderText = Loc.GetString("newspaper-block-empty");
                _text.SetEditable(_page.Editable && !_page.LayoutMode && _block.Kind is NewspaperBlockKind.Text or NewspaperBlockKind.PublicationName or NewspaperBlockKind.PageNumber);
                var text = _block.Kind == NewspaperBlockKind.EditionNumber
                    ? (_page._edition!.Number > 0 ? Loc.GetString("newspaper-edition", ("edition", _page._edition.Number)) : "")
                    : _block.Kind == NewspaperBlockKind.PublicationName ? _page._edition!.Name
                    : _block.Kind == NewspaperBlockKind.PageNumber ? $"{_block.Page + 1} / {_page._edition!.Pages}" : _block.Text;
                if (_text.Text != text)
                    _text.LoadText(text);
                _text.SetContent(text, _block.Ink);
            }
            if (_photo != null && _placeholder != null)
            {
                _photo.Texture = photo;
                _photoShader ??= IoCManager.Resolve<IPrototypeManager>().Index<ShaderPrototype>("N14NewspaperPhoto").InstanceUnique();
                _photoShader.SetParameter("Grayscale", _block.Grayscale);
                _photoShader.SetParameter("EdgeWidth", Math.Clamp(2f / Math.Max(1, Math.Min(_block.Width, _block.Height)), 0.004f, 0.03f));
                _photo.ShaderOverride = _photoShader;
                _placeholder.DisplayScale = _page.PreviewScale;
                _placeholder.BlockSize = new Vector2(_block.Width, _block.Height);
                _placeholder.FontSize = 14;
                _placeholder.Centered = true;
                _placeholder.SetContent(Loc.GetString("newspaper-full-photo-slot"));
                _placeholder.Visible = _page.Editable && photo == null;
            }
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (_page.Editable && _page.LayoutMode && _page.Selected != _index)
                handle.DrawRect(PixelSizeBox, Color.FromHex("#A79B81"), false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _photoShader?.Dispose();
            base.Dispose(disposing);
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (!_page.Editable || args.Function != EngineKeyFunctions.UIClick)
                return;
            _page.Selected = _index;
            _page.OnSelected?.Invoke(_index);
            args.Handle();
            if (!_page.LayoutMode) return;
            _dragging = true;
            UserInterfaceManager.ControlFocused = this;
            _resizing = args.RelativePosition.X >= Width - 10 && args.RelativePosition.Y >= Height - 10;
            _startPointer = args.PointerLocation.Position / UIScale;
            _startPosition = new Vector2(_block.X, _block.Y);
            _startSize = new Vector2(_block.Width, _block.Height);
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if (args.Function != EngineKeyFunctions.UIClick)
                return;
            _dragging = false;
            if (UserInterfaceManager.ControlFocused == this)
                UserInterfaceManager.ControlFocused = null;
        }

        protected override void ControlFocusExited()
        {
            base.ControlFocusExited();
            _dragging = false;
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            if (!_dragging || _page._edition == null)
                return;
            var delta = (args.GlobalPosition - _startPointer) / _page.PreviewScale;
            if (delta.LengthSquared() > 0 && _block.Kind == NewspaperBlockKind.Text) _block.CaptionFor = null;
            int Snap(float value) => (int)Math.Round(value / (_page.Snap ? 10 : 1)) * (_page.Snap ? 10 : 1);
            if (_resizing)
            {
                _block.Width = Math.Clamp(Snap(_startSize.X + delta.X), 1, _page._edition.Width - _block.X);
                _block.Height = Math.Clamp(Snap(_startSize.Y + delta.Y), 1, _page._edition.Height - _block.Y);
            }
            else
            {
                _block.X = Math.Clamp(Snap(_startPosition.X + delta.X), 0, _page._edition.Width - _block.Width);
                _block.Y = Math.Clamp(Snap(_startPosition.Y + delta.Y), 0, _page._edition.Height - _block.Height);
            }
            NewspaperLayout.ResolvePhotoCaptions(_page._edition);
            Update(_photo?.Texture);
            _page.OnChanged?.Invoke();
        }
    }
}
