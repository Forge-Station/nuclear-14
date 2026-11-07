using Content.Shared._Forge.Paper;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Forge.Paper;

/// <summary>Reusable paper background for document windows, using the same bounded cache as newspapers.</summary>
public sealed class PaperSurfacePanel : PanelContainer
{
    private readonly PaperSurfaceRenderer _surface = new();
    private PaperSurfaceAppearance? _appearance;

    public void SetSurface(PaperSurfaceAppearance? appearance)
    {
        _appearance = appearance?.Clone();
        if (_appearance == null) _surface.Dispose();
        else _surface.Update(_appearance, 510, 660, 0);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_appearance != null && _surface.NeedsUpdate)
            _surface.Update(_appearance, 510, 660, 0);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (_appearance == null) { base.Draw(handle); return; }
        _surface.DrawBackground(handle, PixelSizeBox);
        _surface.DrawOverlay(handle, PixelSizeBox);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _surface.Dispose();
        base.Dispose(disposing);
    }
}
