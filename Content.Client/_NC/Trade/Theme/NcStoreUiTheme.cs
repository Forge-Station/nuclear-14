using Robust.Client.Graphics;


namespace Content.Client._NC.Trade.Theme;


public static class NcStoreUiTheme
{
    public static Color ResolveColor(string? value, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                return Color.FromHex(value);
            }
            catch
            {
                // Keep UI usable even if YAML contains an invalid color token.
            }
        }

        return Color.FromHex(fallback);
    }

    public static Color WithAlpha(Color color, float alpha)
    {
        alpha = Math.Clamp(alpha, 0f, 1f);
        return new(color.R, color.G, color.B, alpha);
    }

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            a.A + (b.A - a.A) * t);
    }

    public static bool IsTooDark(Color color)
    {
        var luma = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
        return luma < 0.20f;
    }

    public static StyleBoxFlat Flat(Color background, Color border) =>
        new()
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new(1)
        };

    public static StyleBoxFlat Flat(Color background, Color border, Thickness thickness) =>
        new()
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = thickness
        };

    public static StyleBoxFlat Fill(Color background) =>
        new()
        {
            BackgroundColor = background,
            BorderColor = new(0f, 0f, 0f, 0f),
            BorderThickness = new(0)
        };
}
