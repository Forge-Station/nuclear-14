using Content.Client._Forge.Paper;
using Content.Shared._Forge.Paper;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client.Paper.UI;

public sealed partial class PaperWindow
{
    private EntityUid? _surfaceOwner;
    private PaperSurfaceStyleBox? _surfaceStyle;
    private StyleBox? _originalSurfaceStyle;
    private Color? _originalSurfaceColor;

    internal void SetSurfaceEntity(EntityUid owner) => _surfaceOwner = owner;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_surfaceOwner is not {} owner) return;
        if (IoCManager.Resolve<IEntityManager>().TryGetComponent<PaperSurfaceComponent>(owner, out var surface))
        {
            if (_surfaceStyle == null)
            {
                _originalSurfaceStyle = PaperBackground.PanelOverride;
                _originalSurfaceColor = PaperBackground.ModulateSelfOverride;
                _surfaceStyle = new PaperSurfaceStyleBox(_originalSurfaceStyle ?? new StyleBoxFlat());
                PaperBackground.PanelOverride = _surfaceStyle;
                PaperBackground.ModulateSelfOverride = Color.White;
            }
            _surfaceStyle.Update(surface.Appearance);
        }
        else if (_surfaceStyle != null)
        {
            PaperBackground.PanelOverride = _originalSurfaceStyle;
            PaperBackground.ModulateSelfOverride = _originalSurfaceColor;
            _surfaceStyle.Dispose();
            _surfaceStyle = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _surfaceStyle?.Dispose();
        base.Dispose(disposing);
    }

    private sealed class PaperSurfaceStyleBox(StyleBox original) : StyleBox(original), IDisposable
    {
        private readonly PaperSurfaceRenderer _renderer = new();
        public void Update(PaperSurfaceAppearance appearance) => _renderer.Update(appearance, 510, 660, 0);
        protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
        {
            _renderer.DrawBackground(handle, box);
            _renderer.DrawOverlay(handle, box);
        }
        public void Dispose() => _renderer.Dispose();
    }
}
