using Content.Shared._Forge.Paper;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.Paper;

/// <summary>Two reusable sheet layers. Owns at most two faces, cached independently of viewport zoom.</summary>
internal sealed class PaperSurfaceRenderer : IDisposable
{
    private (int Seed, string Profile, float Wear, float Fibers, float Impurities, int Width, int Height)? _key;
    private readonly Dictionary<int, DocumentResourceCache<DocumentTextures>.Lease?> _faces = new();
    private int _side;
    public bool NeedsUpdate => !_faces.TryGetValue(_side, out var face) || face == null;
    private Color _color = Color.FromHex("#DDDFD9");

    public void Update(PaperSurfaceAppearance appearance, int width, int height, int side)
    {
        var key = (appearance.Seed, appearance.Profile, appearance.Wear, appearance.Fibers, appearance.Impurities, width, height);
        if (_key != key) { Dispose(); _key = key; }
        _side = side;
        if (_faces.TryGetValue(side, out var face) && face != null) return;
        var profile = IoCManager.Resolve<IPrototypeManager>().Index<PaperSurfacePrototype>(appearance.Profile);
        _color = profile.BaseColor;
        var scale = Math.Min(1f, 512f / Math.Max(width, height));
        var bytes = (long)Math.Max(1, (int)(width * scale)) * Math.Max(1, (int)(height * scale)) * 8;
        var cache = IoCManager.Resolve<IEntityManager>().System<DocumentTextureCacheSystem>();
        var cacheKey = $"paper:{appearance.Seed}:{appearance.Profile}:{BitConverter.SingleToInt32Bits(appearance.Wear)}:{BitConverter.SingleToInt32Bits(appearance.Fibers)}:{BitConverter.SingleToInt32Bits(appearance.Impurities)}:{width}:{height}:{side}";
        _faces[side] = cache.Surface(cacheKey, bytes, appearance, profile, width, height, side);
    }
    public void DrawBackground(DrawingHandleScreen handle, UIBox2 bounds)
    {
        if (_faces.TryGetValue(_side, out var face) && face != null) handle.DrawTextureRect(face.Value.Background, bounds);
        else handle.DrawRect(bounds, _color);
    }
    public void DrawOverlay(DrawingHandleScreen handle, UIBox2 bounds)
    {
        if (_faces.TryGetValue(_side, out var face) && face?.Value.Overlay is {} overlay) handle.DrawTextureRect(overlay, bounds);
    }
    public void Dispose()
    {
        foreach (var face in _faces.Values) face?.Dispose();
        _faces.Clear(); _key = null;
    }
}
