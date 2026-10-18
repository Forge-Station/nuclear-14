// Forge-Change: paper writing in multiple languages
using System.Text;
using Content.Shared.Language;
using Robust.Shared.Prototypes;

namespace Content.Shared.Paper;

public static class PaperLanguageFormatting
{
    public static string Join(IReadOnlyList<PaperLanguageSegment> segments)
    {
        return JoinRange(segments, 0, segments.Count);
    }

    public static string JoinRange(IReadOnlyList<PaperLanguageSegment> segments, int start, int count)
    {
        var builder = new StringBuilder();
        var end = Math.Min(segments.Count, start + count);
        for (var i = start; i < end; i++)
        {
            var text = segments[i].Text;
            if (string.IsNullOrEmpty(text))
                continue;

            if (builder.Length > 0 && builder[^1] != '\n')
                builder.Append('\n');

            builder.Append(text);
        }

        return builder.ToString();
    }

    public static string Combine(string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(prefix))
            return suffix;

        if (string.IsNullOrWhiteSpace(suffix))
            return prefix;

        if (prefix.EndsWith('\n'))
            return prefix + suffix;

        return prefix + "\n" + suffix;
    }

    /// <summary>
    /// Trailing stretches the reader understands may be rewritten.
    /// Everything before that stays locked. Returns how many leading segments are locked.
    /// </summary>
    public static int GetLockedPrefixCount(
        IReadOnlyList<PaperLanguageSegment> segments,
        Func<ProtoId<LanguagePrototype>, bool> canUnderstand)
    {
        var locked = segments.Count;
        for (var i = segments.Count - 1; i >= 0; i--)
        {
            var segment = segments[i];
            if (string.IsNullOrWhiteSpace(segment.Text) || canUnderstand(segment.Language))
            {
                locked = i;
                continue;
            }

            break;
        }

        return locked;
    }

    public static List<PaperLanguageSegment> GetEffectiveSegments(PaperLanguageComponent component, string? fallbackContent)
    {
        if (component.Segments.Count > 0)
            return component.Segments;

        if (string.IsNullOrWhiteSpace(fallbackContent))
            return new List<PaperLanguageSegment>();

        return new List<PaperLanguageSegment>
        {
            new()
            {
                Text = fallbackContent,
                Language = component.Language
            }
        };
    }
}
