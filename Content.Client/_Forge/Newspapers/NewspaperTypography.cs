using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using System.Text;

namespace Content.Client._Forge.Newspapers;

internal static class NewspaperTypography
{
    public static (string Text, int Size, bool Fits) FitBox(string text, FontResource resource,
        int width, int height, int maxSize)
        => FitBox(text, size => new VectorFont(resource, size), width, height, maxSize);

    internal static (string Text, int Size, bool Fits) FitBox(string text, Func<int, Font> createFont,
        int width, int height, int maxSize)
    {
        if (text.Length == 0)
            return (text, maxSize, true);
        var font = createFont(maxSize);
        var lines = Wrap(text, font, Math.Max(1, width - 4));
        var textHeight = font.GetHeight(1) + Math.Max(0, lines.Length - 1) * font.GetLineHeight(1);
        var fits = textHeight <= height - 4 && lines.All(line => Width(line, font) <= width - 4);
        return (string.Join("\n", lines), maxSize, fits);
    }

    private static string[] Wrap(string text, Font font, int maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (Width(candidate, font) <= maxWidth)
                {
                    current.Clear().Append(candidate);
                    continue;
                }

                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                foreach (var rune in word.EnumerateRunes())
                {
                    if (current.Length > 0 && Width(current.ToString() + rune, font) > maxWidth)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }

                    current.Append(rune.ToString());
                }
            }

            lines.Add(current.ToString());
        }

        return lines.ToArray();
    }

    private static float Width(string text, Font font)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
            width += font.GetCharMetrics(rune, 1)?.Advance ?? 0;
        return width;
    }
    internal readonly record struct StyledCharacter(Rune Rune, bool Bold, bool Italic);
    internal sealed record StyledLine(List<StyledCharacter> Characters, float Width, int Ascent, int Height, int LineHeight);
    internal sealed record StyledLayout(List<StyledLine> Lines, bool Fits);

    // Only the two supported inline styles are interpreted. Other tags remain ordinary text.
    internal static List<StyledCharacter> ParseStyles(string text, int start = 0, int end = int.MaxValue)
    {
        var result = new List<StyledCharacter>();
        var bold = 0; var italic = 0;
        for (var index = 0; index < text.Length;)
        {
            var rest = text.AsSpan(index);
            if (rest.StartsWith("[b]")) { bold++; index += 3; continue; }
            if (rest.StartsWith("[/b]")) { bold = Math.Max(0, bold - 1); index += 4; continue; }
            if (rest.StartsWith("[i]")) { italic++; index += 3; continue; }
            if (rest.StartsWith("[/i]")) { italic = Math.Max(0, italic - 1); index += 4; continue; }
            var rune = Rune.TryGetRuneAt(text, index, out var parsed) ? parsed : Rune.ReplacementChar;
            if (index >= start && index < end) result.Add(new StyledCharacter(rune, bold > 0, italic > 0));
            index += rune.Utf16SequenceLength;
        }
        return result;
    }

    internal static (bool Bold, bool Italic) SelectedStyles(string text, int start, int end)
    {
        var chars = start == end ? ParseStyles(text) : ParseStyles(text, start, end);
        return (chars.Count > 0 && chars.All(c => c.Bold), chars.Count > 0 && chars.All(c => c.Italic));
    }

    internal static (string Text, int Start, int End) ToggleStyle(string text, int start, int end, string tag)
    {
        if (start == end) { start = 0; end = text.Length; }
        var open = $"[{tag}]"; var close = $"[/{tag}]";
        if (start >= open.Length && end + close.Length <= text.Length &&
            text.AsSpan(0, start).EndsWith(open) && text.AsSpan(end).StartsWith(close))
            return (text[..(start - open.Length)] + text[start..end] + text[(end + close.Length)..], start - open.Length, end - open.Length);
        var selection = text[start..end];
        if (selection.StartsWith(open) && selection.EndsWith(close) && selection.Length >= open.Length + close.Length)
        {
            var plain = selection[open.Length..^close.Length];
            return (text[..start] + plain + text[end..], start, start + plain.Length);
        }
        return (text[..start] + open + selection + close + text[end..], start + open.Length, end + open.Length);
    }

    internal static StyledLayout LayoutStyled(string text, Func<bool, bool, Font> fontFor, int width, int height, bool markup = true)
    {
        var input = markup ? ParseStyles(text) : text.EnumerateRunes().Select(r => new StyledCharacter(r, false, false)).ToList();
        var lines = new List<List<StyledCharacter>>();
        var current = new List<StyledCharacter>(); var word = new List<StyledCharacter>();
        float Advance(StyledCharacter c) => fontFor(c.Bold, c.Italic).GetCharMetrics(c.Rune, 1)?.Advance ?? 0;
        float Measure(IEnumerable<StyledCharacter> chars) => chars.Sum(Advance);
        var maxWidth = Math.Max(1, width - 4);
        var currentWidth = 0f;
        var separator = new StyledCharacter(new Rune(' '), false, false);
        void EndLine() { lines.Add(current); current = new List<StyledCharacter>(); currentWidth = 0; }
        void EndWord()
        {
            if (word.Count == 0) return;
            var space = separator;
            var wordWidth = Measure(word);
            var spaceWidth = Advance(space);
            if (current.Count > 0 && currentWidth + spaceWidth + wordWidth <= maxWidth)
            { current.Add(space); current.AddRange(word); currentWidth += spaceWidth + wordWidth; word.Clear(); return; }
            if (current.Count > 0) EndLine();
            foreach (var character in word)
            {
                var advance = Advance(character);
                if (current.Count > 0 && currentWidth + advance > maxWidth) EndLine();
                current.Add(character); currentWidth += advance;
            }
            word.Clear();
        }
        foreach (var character in input)
        {
            if (character.Rune.Value == '\r') continue;
            if (character.Rune.Value == '\n') { EndWord(); EndLine(); }
            else if (character.Rune.Value == ' ') { EndWord(); separator = character; }
            else word.Add(character);
        }
        EndWord(); EndLine();
        var output = new List<StyledLine>();
        foreach (var line in lines)
        {
            var fonts = line.Count > 0 ? line.Select(c => fontFor(c.Bold, c.Italic)).Distinct().ToArray() : new[] { fontFor(false, false) };
            output.Add(new StyledLine(line, Measure(line), fonts.Max(f => f.GetAscent(1)),
                fonts.Max(f => f.GetHeight(1)), fonts.Max(f => f.GetLineHeight(1))));
        }
        var textHeight = output.Take(output.Count - 1).Sum(l => l.LineHeight) + output[^1].Height;
        return new StyledLayout(output, text.Length == 0 || textHeight <= height - 4 && output.All(l => l.Width <= width - 4));
    }

}
