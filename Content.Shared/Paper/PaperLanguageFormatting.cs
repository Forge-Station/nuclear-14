// Forge-Change: paper writing in multiple languages
using System.Text;

namespace Content.Shared.Paper;

public static class PaperLanguageFormatting
{
    public static string Join(IReadOnlyList<PaperLanguageSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            if (builder.Length > 0 && builder[^1] != '\n')
                builder.Append('\n');

            builder.Append(segment.Text);
        }

        return builder.ToString();
    }

    public static string GetAppendedFragment(string existing, string submitted)
    {
        if (string.IsNullOrEmpty(existing))
            return submitted;

        if (string.IsNullOrWhiteSpace(submitted))
            return string.Empty;

        if (submitted.StartsWith(existing, StringComparison.Ordinal))
            return submitted[existing.Length..].TrimStart('\r', '\n');

        return submitted;
    }
}
