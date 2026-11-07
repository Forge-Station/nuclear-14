using System.Linq;
using System.Numerics;
using Content.Shared._Forge.Newspapers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Forge.Newspapers;

public sealed partial class NewspaperDeskWindow
{
    private void InitializeBlockEditor()
    {
        foreach (var borders in new[] { NewspaperBorders.None, NewspaperBorders.Horizontal, NewspaperBorders.Vertical, NewspaperBorders.All })
            BordersButton.AddItem(Loc.GetString($"newspaper-borders-{borders.ToString().ToLowerInvariant()}"), (int)borders);
        BordersButton.OnItemSelected += args => EditBlock(b => b.Borders = (NewspaperBorders)args.Id);
        CloseEditorButton.OnPressed += _ =>
        {
            _selectedBlockIndex = -1;
            Refresh();
        };
        foreach (var kind in Enum.GetValues<NewspaperBlockKind>())
            AddKindButton.AddItem(Loc.GetString($"newspaper-kind-{kind.ToString().ToLowerInvariant()}"), (int)kind);
        foreach (var font in new[] { NewspaperFont.Serif, NewspaperFont.Sans, NewspaperFont.Cambria })
            FontButton.AddItem(Loc.GetString($"newspaper-font-{font.ToString().ToLowerInvariant()}"), (int)font);
        WeightButton.AddItem(Loc.GetString("newspaper-weight-regular"), 0);
        WeightButton.AddItem(Loc.GetString("newspaper-weight-semibold"), 1);
        WeightButton.AddItem(Loc.GetString("newspaper-weight-bold"), 2);
        WeightButton.OnItemSelected += args => EditBlock(b => b.Font = FontWithWeight(FontFamily(b.Font), args.Id));
        foreach (var ink in Enum.GetValues<NewspaperInk>())
            InkButton.AddItem(Loc.GetString($"newspaper-ink-{ink.ToString().ToLowerInvariant()}"), (int)ink);
        AddKindButton.SelectId(0);
        BindSpin(PageWidth, NewspaperLayout.MinPageSize, NewspaperLayout.MaxPageSize, v => { _draft.Width = v; ClampBlocks(); });
        BindSpin(PageHeight, NewspaperLayout.MinPageSize, NewspaperLayout.MaxPageSize, v => { _draft.Height = v; ClampBlocks(); });
        BindSpin(PageCount, 1, 2, v => { _draft.Pages = v; ClampBlocks(); });
        BindSpin(BlockX, 0, 1200, v => { if (Selected is {} b) { b.CaptionFor = null; b.X = Math.Min(v, _draft.Width - b.Width); } });
        BindSpin(BlockY, 0, 1200, v => { if (Selected is {} b) { b.CaptionFor = null; b.Y = Math.Min(v, _draft.Height - b.Height); } });
        BindSpin(BlockWidth, 1, 1200, v => { if (Selected is {} b) { b.CaptionFor = null; b.Width = Math.Min(v, _draft.Width - b.X); } });
        BindSpin(BlockHeight, 1, 1200, v => { if (Selected is {} b) b.Height = Math.Min(v, _draft.Height - b.Y); });
        BindSpin(BlockPage, 1, 2, v =>
        {
            if (Selected is not {} block) return;
            block.Page = Math.Min(v - 1, _draft.Pages - 1); _page = block.Page;
            if (block.Kind == NewspaperBlockKind.Photo)
                foreach (var caption in _draft.Blocks.Where(c => block.Id.Length > 0 && c.CaptionFor == block.Id)) caption.Page = block.Page;
            else block.CaptionFor = null;
        });
        BindSpin(PhotoSizeField, 32, 1200, value =>
        {
            if (Selected is not { Kind: NewspaperBlockKind.Photo } photo) return;
            var captionSpace = _draft.Blocks.Where(b => b.CaptionFor == photo.Id && photo.Id.Length > 0).Select(b => b.Height + 6).DefaultIfEmpty(0).Max();
            var side = Math.Min(value, Math.Min(_draft.Width - photo.X, _draft.Height - photo.Y - captionSpace));
            photo.Width = photo.Height = Math.Max(1, side);
            NewspaperLayout.ResolvePhotoCaptions(_draft);
        });
        GrayscaleButton.OnToggled += _ => { if (!_loading && Selected is { Kind: NewspaperBlockKind.Photo } photo) { photo.Grayscale = GrayscaleButton.Pressed; Refresh(); } };
        BoldButton.OnPressed += _ => FormatSelection("b");
        ItalicButton.OnPressed += _ => FormatSelection("i");
        BindSpin(FontSizeField, 1, 72, v => { if (Selected is {} b) b.FontSize = v; });
        TextField.OnTextChanged += _ => EditBlock(b => b.Text = Rope.Collapse(TextField.TextRope), typing: true);
        FontButton.OnItemSelected += args => EditBlock(b => b.Font = FontWithWeight((NewspaperFont)args.Id, FontWeight(b.Font)));
        InkButton.OnItemSelected += args => EditBlock(b => b.Ink = (NewspaperInk)args.Id);
        CenterButton.OnToggled += _ => EditBlock(b => b.Centered = CenterButton.Pressed);
        BlockList.OnItemSelected += args =>
        {
            _pageSettings = false; _selectedBlockIndex = args.Id;
            _typingBlock = -1;
            ClearReplacementConfirmation();
            if (_selectedBlockIndex >= 0) _page = _draft.Blocks[_selectedBlockIndex].Page;
            Refresh();
        };
        AddButton.OnPressed += _ =>
        {
            _pageSettings = false;
            if (_draft.Blocks.Count >= NewspaperLayout.MaxBlocks) return;
            var kind = (NewspaperBlockKind)AddKindButton.SelectedId;
            _draft.Blocks.Add(new NewspaperBlock { Id = Guid.NewGuid().ToString("N"), Kind = kind, Page = _page, X = 20, Y = 20, Width = 200, Height = kind == NewspaperBlockKind.Line ? 1 : 100 });
            _selectedBlockIndex = _draft.Blocks.Count - 1;
            RefreshBlockList(); Refresh();
        };
        DeleteButton.OnPressed += _ =>
        {
            if (Selected == null) return;
            foreach (var caption in _draft.Blocks.Where(c => c.CaptionFor == Selected.Id && Selected.Id.Length > 0)) caption.CaptionFor = null;
            _draft.Blocks.RemoveAt(_selectedBlockIndex);
            _selectedBlockIndex = Math.Min(_selectedBlockIndex, _draft.Blocks.Count - 1);
            RefreshBlockList(); Refresh();
        };
        DuplicateButton.OnPressed += _ =>
        {
            if (Selected is not {} b || _draft.Blocks.Count >= NewspaperLayout.MaxBlocks) return;
            var copy = b.Clone(); copy.Id = Guid.NewGuid().ToString("N"); copy.CaptionFor = null; copy.X = Math.Min(copy.X + 10, _draft.Width - copy.Width); copy.Y = Math.Min(copy.Y + 10, _draft.Height - copy.Height);
            _draft.Blocks.Add(copy); _selectedBlockIndex = _draft.Blocks.Count - 1;
            RefreshBlockList(); Refresh();
        };
    }

    private void RefreshBlockEditor()
    {
        AutomaticBlockLabel.Visible = Selected?.Kind is NewspaperBlockKind.PageNumber or NewspaperBlockKind.EditionNumber;
        PageWidth.OverrideValue(_draft.Width); PageHeight.OverrideValue(_draft.Height); PageCount.OverrideValue(_draft.Pages);
        if (Selected is {} b)
        {
            BlockList.SelectId(_selectedBlockIndex);
            BlockX.OverrideValue(b.X); BlockY.OverrideValue(b.Y); BlockWidth.OverrideValue(b.Width); BlockHeight.OverrideValue(b.Height); BlockPage.OverrideValue(b.Page + 1);
            BordersButton.TrySelectId((int)b.Borders);
            FontSizeField.OverrideValue(b.FontSize); FontButton.SelectId((int)FontFamily(b.Font)); WeightButton.SelectId(FontWeight(b.Font));
            WeightButton.SetItemDisabled(1, FontFamily(b.Font) != NewspaperFont.Serif);
            WeightButton.SetItemDisabled(2, FontFamily(b.Font) == NewspaperFont.Cambria); InkButton.SelectId((int)b.Ink); CenterButton.Pressed = b.Centered;
            if (Rope.Collapse(TextField.TextRope) != b.Text) TextField.TextRope = new Rope.Leaf(b.Text);
            TextProperties.Visible = b.Kind is NewspaperBlockKind.Text or NewspaperBlockKind.PublicationName or NewspaperBlockKind.EditionNumber or NewspaperBlockKind.PageNumber;
            TextInputProperties.Visible = EmphasisToolbar.Visible = SelectionHintLabel.Visible = b.Kind == NewspaperBlockKind.Text;
            AutomaticBlockLabel.Visible = b.Kind is NewspaperBlockKind.PageNumber or NewspaperBlockKind.EditionNumber;
            PhotoProperties.Visible = b.Kind == NewspaperBlockKind.Photo;
            if (b.Kind == NewspaperBlockKind.Photo) { PhotoSizeField.OverrideValue(b.Width); GrayscaleButton.Pressed = b.Grayscale; }
        }
        // Reserve the editing column throughout both editing modes. Selecting a block does not change zoom.
        EditorSidebar.Visible = !_published && !PhotoBufferButton.Pressed;
        EmptySelectionLabel.Visible = Selected == null && (!_layout || !_pageSettings);
        SelectedBlockLabel.Visible = CloseEditorButton.Visible = Selected != null && (!_layout || !_pageSettings);
        if (Selected is {} selected) SelectedBlockLabel.Text = SelectedBlockLabel.ToolTip = BlockDescription(selected);
        AdvancedTextProperties.Visible = TextAppearanceButton.Pressed || _layout;
    }

    private void EditBlock(Action<NewspaperBlock> edit, bool typing = false)
    {
        if (_loading || _published || Selected is not {} block) return;
        _typingChange = typing;
        try { edit(block); Refresh(); }
        finally { _typingChange = false; }
    }

    private static NewspaperFont FontFamily(NewspaperFont font) => font switch
    {
        NewspaperFont.SansBold => NewspaperFont.Sans,
        NewspaperFont.SerifBold or NewspaperFont.SerifSemibold => NewspaperFont.Serif,
        _ => font,
    };
    private static int FontWeight(NewspaperFont font) => font switch
    {
        NewspaperFont.SansBold or NewspaperFont.SerifBold => 2,
        NewspaperFont.SerifSemibold => 1,
        _ => 0,
    };
    private static NewspaperFont FontWithWeight(NewspaperFont family, int weight) => family switch
    {
        NewspaperFont.Serif when weight == 2 => NewspaperFont.SerifBold,
        NewspaperFont.Serif when weight == 1 => NewspaperFont.SerifSemibold,
        NewspaperFont.Sans when weight > 0 => NewspaperFont.SansBold,
        _ => family,
    };

    private static string PlainText(string text) => string.Concat(NewspaperTypography.ParseStyles(text).Select(c => c.Rune.ToString()));

    private static string ShortText(string text, int limit) => text.Length > limit ? text[..limit] + "…" : text;

    private static string BlockDescription(NewspaperBlock block)
    {
        var text = PlainText(block.Text).Replace('\n', ' ').Trim();
        text = ShortText(text, 22);
        var kind = Loc.GetString($"newspaper-kind-{block.Kind.ToString().ToLowerInvariant()}");
        return $"{kind}: {(text.Length == 0 ? "—" : text)} ({block.Page + 1})";
    }

    private static string EditionHeadline(NewspaperEdition edition) => edition.Blocks
        .Where(b => b.Kind == NewspaperBlockKind.Text && !string.IsNullOrWhiteSpace(b.Text) && b.TextKey == null)
        .OrderByDescending(b => b.FontSize).Select(b => PlainText(b.Text).Replace('\n', ' ')).FirstOrDefault() ?? edition.Name;

    private void BindSpin(SpinBox box, int min, int max, Action<int> action)
    {
        box.IsValid = v => v >= min && v <= max;
        box.InitDefaultButtons();
        box.LineEditControl.MinWidth = 40;
        box.ValueChanged += args => { if (!_loading && !_published) { action(args.Value); Refresh(); } };
    }

    private void FindOverflowBlock()
    {
        var index = PageView.OverflowBlock;
        if (index < 0) return;
        _pageSettings = false;
        _selectedBlockIndex = index;
        _page = _draft.Blocks[index].Page;
        Refresh();
        PageViewport.SetScrollValue(new Vector2(Math.Max(0, _draft.Blocks[index].X * PageView.PreviewScale - 10),
            Math.Max(0, _draft.Blocks[index].Y * PageView.PreviewScale - 10)));
    }

    private void LinkSelectedCaption()
    {
        if (Selected is not { Kind: NewspaperBlockKind.Text } caption) return;
        var photo = _draft.Blocks.Where(b => b.Kind == NewspaperBlockKind.Photo && b.Page == caption.Page)
            .OrderBy(b => Math.Abs(b.X - caption.X) + Math.Abs(b.Y + b.Height - caption.Y)).FirstOrDefault();
        if (photo == null) return;
        if (photo.Id.Length == 0) photo.Id = Guid.NewGuid().ToString("N");
        caption.CaptionFor = photo.Id;
        caption.Centered = true;
        Refresh();
    }

    private void LoadSelectedTemplate()
    {
        var photo = _draft.PhotoId;
        var loaded = _selectedTemplateIndex < _starters.Count ? CreateStarter(_starters[_selectedTemplateIndex], false) : _state!.Templates[_selectedTemplateIndex - _starters.Count].Clone();
        loaded.PhotoId = photo;
        loaded.Name = _draft.Name;
        loaded.PublicationId = _draft.PublicationId;
        SetDraft(loaded);
    }

    private void LoadSelectedExample()
    {
        if (_selectedTemplateIndex >= _starters.Count) return;
        var starter = _starters[Math.Clamp(_selectedTemplateIndex, 0, _starters.Count - 1)];
        var demo = CreateStarter(starter, true);
        demo.PhotoId = _draft.PhotoId;
        demo.PublicationId = _draft.PublicationId;
        if (!string.IsNullOrWhiteSpace(_draft.Name)) demo.Name = _draft.Name;
        SetDraft(demo);
    }

    private void SaveCurrentTemplate()
    {
        if (_state!.Templates.Any(t => t.TemplateName == _draft.TemplateName.Trim()) && !ConfirmButton(SaveTemplateButton, Loc.GetString("newspaper-confirm-template"))) return;
        OnSaveTemplate?.Invoke(_draft.Clone());
    }

    private void SelectPageBlock(int index)
    {
        _pageSettings = false;
        ClearReplacementConfirmation();
        _selectedBlockIndex = index;
        _typingBlock = -1;
        if (Selected?.Kind != NewspaperBlockKind.Photo) PhotoBufferButton.Pressed = false;
        if (Selected?.Kind == NewspaperBlockKind.Photo) _selectedGalleryPhotoId = _draft.PhotoFor(Selected);

        Refresh();
        if (!_layout)
        {
            if (Selected?.Kind == NewspaperBlockKind.PublicationName) OpenRename();
            else if (Selected?.Kind == NewspaperBlockKind.Text) TextField.GrabKeyboardFocus();
        }
    }
}
