using System.Linq;
using System.Numerics;
using Content.Shared._Forge.Newspapers;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Forge.Newspapers;

public sealed partial class NewspaperDeskWindow
{
    private int _selectedGalleryPhotoId = -1;
    private readonly Dictionary<int, (Button Button, TextureRect Preview)> _photoCards = new();
    private HashSet<int> _visibleGalleryPhotos = new();

    private void PollVisibleGallery()
    {
        var top = PhotoGalleryScroll.GlobalPosition.Y;
        var bottom = top + PhotoGalleryScroll.Height;
        var visible = PhotoBufferToolbar.Visible
            ? _photoCards.Where(p => p.Value.Button.Height > 0 && p.Value.Button.GlobalPosition.Y < bottom &&
                p.Value.Button.GlobalPosition.Y + p.Value.Button.Height > top).Select(p => p.Key).ToHashSet()
            : new HashSet<int>();
        if (_visibleGalleryPhotos.SetEquals(visible)) return;
        _visibleGalleryPhotos = visible;
        Refresh();
    }

    private void RefreshPhotoGallery()
    {
        if (_state == null) return;
        var ids = _state.BufferedPhotos;
        if (!_photoCards.Keys.SequenceEqual(ids))
        {
            ClearReplacementConfirmation();
            PhotoGalleryItems.DisposeAllChildren();
            _photoCards.Clear();
            foreach (var id in ids)
            {
                var button = new Button { HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 8) };
                var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
                var preview = new TextureRect
                {
                    CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    MinSize = new Vector2(0, 150), MaxHeight = 150,
                    MouseFilter = MouseFilterMode.Ignore,
                };
                content.AddChild(preview);
                content.AddChild(new Label { Text = Loc.GetString("newspaper-photo-entry", ("id", id)) });
                button.AddChild(content);
                button.OnPressed += _ =>
                {
                    ClearReplacementConfirmation();
                    _selectedGalleryPhotoId = id;
                    Refresh();
                };
                PhotoGalleryItems.AddChild(button);
                _photoCards.Add(id, (button, preview));
            }
        }
        if (!ids.Contains(_selectedGalleryPhotoId)) _selectedGalleryPhotoId = -1;
        PhotoGalleryEmptyLabel.Visible = ids.Length == 0;
        PhotoTargetLabel.Text = Selected?.Kind == NewspaperBlockKind.Photo
            ? Loc.GetString("newspaper-photo-target", ("block", _selectedBlockIndex + 1), ("page", Selected.Page + 1))
            : Loc.GetString("newspaper-photo-select-block");
        PhotoTargetLabel.ToolTip = PhotoTargetLabel.Text;
        PhotoTargetList.Clear();
        PhotoTargetList.AddItem(Loc.GetString("newspaper-block-none"), -1);
        for (var i = 0; i < _draft.Blocks.Count; i++)
            if (_draft.Blocks[i].Kind == NewspaperBlockKind.Photo)
                PhotoTargetList.AddItem(Loc.GetString("newspaper-photo-target", ("block", i + 1), ("page", _draft.Blocks[i].Page + 1)), i);
        PhotoTargetList.TrySelectId(Selected?.Kind == NewspaperBlockKind.Photo ? _selectedBlockIndex : -1);
        InsertPhotoButton.Disabled = Selected?.Kind != NewspaperBlockKind.Photo || !ids.Contains(_selectedGalleryPhotoId);
        foreach (var (id, card) in _photoCards)
        {
            card.Preview.Texture = PhotoBufferToolbar.Visible && _visibleGalleryPhotos.Contains(id)
                ? _photos.Get(id, _state.PhotoKeys.GetValueOrDefault(id)) : null;
            card.Button.StyleBoxOverride = id == _selectedGalleryPhotoId ? GallerySelectionStyle : null;
            card.Button.ToolTip = Loc.GetString("newspaper-photo-select-preview");
        }
    }

    private void ChoosePhotoForSelectedBlock()
    {
        ClearReplacementConfirmation();
        PhotoBufferButton.Pressed = true;
        if (Selected?.Kind == NewspaperBlockKind.Photo) _selectedGalleryPhotoId = _draft.PhotoFor(Selected);
        Refresh();
    }

    private void SelectPhotoTarget(int blockIndex)
    {
        if (_loading) return;
        ClearReplacementConfirmation();
        _selectedBlockIndex = blockIndex;
        _typingBlock = -1;
        if (Selected != null) _page = Selected.Page;
        Refresh();
    }

    private void ForgetSelectedPhoto()
    {
        var id = _selectedGalleryPhotoId;
        foreach (var block in _draft.Blocks.Where(b => b.Kind == NewspaperBlockKind.Photo && _draft.PhotoFor(b) == id)) block.PhotoId = -1;
        OnForgetPhoto?.Invoke(id);
        Refresh();
    }
}
