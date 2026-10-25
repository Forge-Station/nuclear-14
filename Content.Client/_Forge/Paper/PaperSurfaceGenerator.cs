using Content.Shared._Forge.Paper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Paper;

/// <summary>Deterministic, bounded surface layers. No GPU or UI dependencies.</summary>
internal static class PaperSurfaceGenerator
{
    private readonly record struct Smudge(float X, float Y, float Radius, float Strength);
    private readonly record struct Crease(bool Vertical, float Position, float Slope);
    private readonly record struct Wrinkle(float X, float Y, float Dx, float Dy, float Length);

    // Explicit hash rather than System.Random: a seed has the same result across runtime versions.
    private static uint Hash(uint value)
    {
        unchecked
        {
            value ^= value >> 16; value *= 0x7feb352d;
            value ^= value >> 15; value *= 0x846ca68b;
            return value ^ (value >> 16);
        }
    }
    private static float Random(uint seed, uint index) => (Hash(unchecked(seed + index * 0x9e3779b9)) & 0xffffff) / 16777215f;
    private static float Noise(uint seed, int x, int y) => Random(seed, unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663)));
    private static float SmoothNoise(uint seed, float x, float y)
    {
        var ix = (int)MathF.Floor(x); var iy = (int)MathF.Floor(y);
        var fx = x - ix; var fy = y - iy;
        fx *= fx * (3 - 2 * fx); fy *= fy * (3 - 2 * fy);
        var a = Noise(seed, ix, iy) * (1 - fx) + Noise(seed, ix + 1, iy) * fx;
        var b = Noise(seed, ix, iy + 1) * (1 - fx) + Noise(seed, ix + 1, iy + 1) * fx;
        return a * (1 - fy) + b * fy;
    }

    internal static (Image<Rgba32> Background, Image<Rgba32> Overlay) Generate(
        PaperSurfaceAppearance appearance, PaperSurfacePrototype profile, int sheetWidth, int sheetHeight, int side)
    {
        // Fixed logical resolution: viewport zoom does not change the pattern or allocate new textures.
        var scale = Math.Min(1f, 512f / Math.Max(sheetWidth, sheetHeight));
        var width = Math.Max(1, (int)(sheetWidth * scale));
        var height = Math.Max(1, (int)(sheetHeight * scale));
        var background = new Image<Rgba32>(width, height);
        // The content sandbox permits writing ImageSharp pixels, but not reading
        // the image indexer. Compose material marks in a bounded CPU buffer first.
        var pixels = new Rgba32[width * height];
        var overlay = new Image<Rgba32>(width, height);
        var seed = unchecked((uint)appearance.Seed);
        var faceSeed = side == 0 ? seed : Hash(seed ^ 0xa53c9e1d);
        var wear = Math.Clamp(float.IsFinite(appearance.Wear) ? appearance.Wear : 0, 0, 1);
        var smudges = new Smudge[Math.Clamp(profile.Smudges, 0, 16)];
        for (var i = 0; i < smudges.Length; i++)
        {
            var index = (uint)(100 + i * 5);
            var edge = (int)(Random(faceSeed, index) * 3.99f);
            var along = Random(faceSeed, index + 1);
            var inset = Random(faceSeed, index + 2) * 0.07f;
            smudges[i] = new Smudge(edge == 0 ? inset : edge == 1 ? 1 - inset : along,
                edge == 2 ? inset : edge == 3 ? 1 - inset : along,
                0.03f + Random(faceSeed, index + 3) * 0.08f, Random(faceSeed, index + 4));
        }
        var creases = new Crease[Math.Clamp(profile.Creases, 0, 4)];
        for (var i = 0; i < creases.Length; i++)
            creases[i] = new Crease(i % 2 == 1, 0.3f + Random(seed, (uint)(20 + i * 2)) * 0.4f,
                (Random(seed, (uint)(21 + i * 2)) - 0.5f) * 0.025f);
        var wrinkles = new Wrinkle[Math.Clamp(profile.Wrinkles, 0, 12)];
        for (var i = 0; i < wrinkles.Length; i++)
        {
            var index = (uint)(300 + i * 4);
            var right = Random(seed, index) > 0.5f;
            var bottom = Random(seed, index + 1) > 0.5f;
            var inset = 0.015f + Random(seed, index + 2) * 0.07f;
            var angle = 0.35f + Random(seed, index + 3) * 0.85f;
            wrinkles[i] = new Wrinkle(right ? 1 - inset : inset, bottom ? 1 - inset : inset,
                MathF.Cos(angle) * (right ? -1 : 1), MathF.Sin(angle) * (bottom ? -1 : 1),
                0.045f + Random(seed, index + 4) * 0.06f);
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = (x + 0.5f) / width; var v = (y + 0.5f) / height;
            // Viewing the reverse mirrors sheet geometry. Dirt is independent on each face.
            var physicalU = side == 0 ? u : 1 - u;
            var edge = Math.Min(Math.Min(u, 1 - u), Math.Min(v, 1 - v));
            var edgeFactor = Math.Clamp(1 - edge / 0.045f, 0, 1);
            var grain = (Noise(faceSeed, x, y) - 0.5f) * profile.Grain;
            var mottling = (SmoothNoise(faceSeed, u * 9, v * 9) - 0.5f) * profile.Discoloration;
            var stain = edgeFactor * profile.EdgeWear * (0.2f + SmoothNoise(faceSeed, u * 45, v * 45) * 0.8f);
            foreach (var smudge in smudges)
            {
                var dx = (u - smudge.X) / smudge.Radius; var dy = (v - smudge.Y) / smudge.Radius;
                var falloff = Math.Max(0, 1 - (dx * dx + dy * dy));
                stain += falloff * falloff * smudge.Strength * profile.EdgeWear;
            }
            var tone = wear * (grain + mottling - stain);
            // Geometry uses the sheet seed on both faces. Keep damage inside the blank outer margin.
            var roughness = Math.Clamp(profile.EdgeRoughness, 0, 0.012f) * wear;
            var edgeDistance = Math.Min(
                Math.Min(physicalU - roughness * SmoothNoise(seed, 0, v * 65),
                    1 - physicalU - roughness * SmoothNoise(seed, 15, v * 65)),
                Math.Min(v - roughness * SmoothNoise(seed, physicalU * 65, 0),
                    1 - v - roughness * SmoothNoise(seed, physicalU * 65, 15)));
            var cornerDistance = Math.Min(Math.Min(physicalU + v, 1 - physicalU + v),
                Math.Min(physicalU + 1 - v, 2 - physicalU - v)) - Math.Clamp(profile.CornerWear, 0, 0.035f) * wear;
            var paperAlpha = wear == 0 ? 1 : Math.Clamp(Math.Min(edgeDistance, cornerDistance) * Math.Min(width, height) + 0.5f, 0, 1);
            pixels[y * width + x] = new Rgba32(Byte(profile.BaseColor.R + tone), Byte(profile.BaseColor.G + tone), Byte(profile.BaseColor.B + tone), Byte(paperAlpha));
            var fold = 0f;
            foreach (var crease in creases)
            {
                var across = crease.Vertical ? physicalU : v;
                var along = crease.Vertical ? v : physicalU;
                var distance = across - crease.Position - (along - 0.5f) * crease.Slope;
                var ridge = Math.Max(0, 1 - Math.Abs(distance) / 0.0035f);
                var shadow = Math.Max(0, 1 - Math.Abs(distance - 0.004f) / 0.009f);
                fold += (ridge - shadow * 0.6f) * wear * profile.CreaseStrength;
            }
            foreach (var wrinkle in wrinkles)
            {
                var dx = physicalU - wrinkle.X; var dy = v - wrinkle.Y;
                var along = dx * wrinkle.Dx + dy * wrinkle.Dy;
                if (along < 0 || along > wrinkle.Length) continue;
                var distance = dx * -wrinkle.Dy + dy * wrinkle.Dx;
                var taper = MathF.Sin(along / wrinkle.Length * MathF.PI);
                var ridge = Math.Max(0, 1 - Math.Abs(distance) / 0.002f);
                var shadow = Math.Max(0, 1 - Math.Abs(distance - 0.003f) / 0.006f);
                fold += (ridge - shadow * 0.5f) * taper * wear * profile.CreaseStrength * 0.7f;
            }
            // Cap damage above printed contents; stains and grain live below the ink.
            var alpha = Math.Clamp(Math.Abs(fold), 0, 0.1f);
            var ink = fold >= 0 ? (byte)255 : (byte)40;
            overlay[x, y] = new Rgba32(ink, ink, ink, Byte(alpha * paperAlpha));
        }
        // Recycled pulp is part of the material, not wear. Small raster marks avoid
        // testing thousands of fibers against every pixel and stay beneath the ink.
        var fibers = Math.Clamp(float.IsFinite(appearance.Fibers) ? appearance.Fibers : 0, 0, 1);
        var impurities = Math.Clamp(float.IsFinite(appearance.Impurities) ? appearance.Impurities : 0, 0, 1);
        var areaScale = width * height / (512f * 512f);
        var fiberCount = (int)(Math.Clamp(profile.FiberCount, 0, 4000) * areaScale * fibers);
        for (var i = 0; i < fiberCount; i++)
        {
            var index = (uint)(1000 + i * 6);
            var x = Random(faceSeed, index) * (width - 1);
            var y = Random(faceSeed, index + 1) * (height - 1);
            var angle = Random(faceSeed, index + 2) * MathF.Tau;
            var length = 1.5f + Random(faceSeed, index + 3) * 4;
            var strength = Math.Clamp(profile.FiberStrength, 0, 0.2f) * fibers *
                (0.35f + Random(faceSeed, index + 4) * 0.65f);
            // Both pale fibers and darker flecks occur in unbleached pulp.
            if (Random(faceSeed, index + 5) < 0.25f) strength *= -0.7f;
            for (var step = 0; step <= (int)length; step++)
                Tint(pixels, width, height, (int)(x + MathF.Cos(angle) * step),
                    (int)(y + MathF.Sin(angle) * step), strength * MathF.Sin((step + 1) / (length + 2) * MathF.PI));
        }
        var impurityCount = (int)(Math.Clamp(profile.ImpurityCount, 0, 2000) * areaScale * impurities);
        for (var i = 0; i < impurityCount; i++)
        {
            var index = (uint)(40000 + i * 4);
            var x = (int)(Random(faceSeed, index) * (width - 1));
            var y = (int)(Random(faceSeed, index + 1) * (height - 1));
            var strength = Math.Clamp(profile.ImpurityStrength, 0, 0.25f) * impurities *
                (0.3f + Random(faceSeed, index + 2) * 0.7f);
            Tint(pixels, width, height, x, y, strength);
            if (Random(faceSeed, index + 3) > 0.7f) Tint(pixels, width, height, x + 1, y, strength * 0.5f);
        }
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            background[x, y] = pixels[y * width + x];
        return (background, overlay);
    }
    private static void Tint(Rgba32[] pixels, int width, int height, int x, int y, float strength)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        var pixel = pixels[y * width + x];
        pixels[y * width + x] = new Rgba32(Byte(pixel.R / 255f - strength),
            Byte(pixel.G / 255f - strength * 0.95f), Byte(pixel.B / 255f - strength * 0.85f), pixel.A);
    }
    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
