// Forge-Change: paper writing in multiple languages
using System.Text;
using Content.Client.Language.Systems;
using Content.Shared.Ghost;
using Content.Shared.Language;
using Content.Shared.Language.Systems;
using Content.Shared.Paper;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Shared.Paper.SharedPaperComponent;

namespace Content.Client.Paper;

/// <summary>
/// Obfuscates paper stretches written in languages the local player does not understand.
/// </summary>
public sealed class PaperLanguageVisualsSystem : EntitySystem
{
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public bool TryFormatForReader(EntityUid paper, PaperBoundUserInterfaceState state, out PaperBoundUserInterfaceState filtered)
    {
        filtered = state;

        if (!TryComp<PaperLanguageComponent>(paper, out var paperLang))
            return false;

        if (!HasLanguageState())
            return false;

        var display = FormatSegments(paperLang, state.Text);
        if (display == null || display == state.Text)
            return false;

        filtered = new PaperBoundUserInterfaceState(display, state.StampedBy, state.Mode);
        return true;
    }

    public List<(string Id, string Name)> GetWritableLanguages(EntityUid paper, out string? selected)
    {
        selected = null;
        var result = new List<(string Id, string Name)>();

        if (!TryComp<PaperLanguageComponent>(paper, out var paperLang))
            return result;

        var spoken = _language.GetLocalSpeaker()?.SpokenLanguages ?? [];
        var ghostWriter = _player.LocalEntity is { } local && HasComp<GhostComponent>(local);

        if (ghostWriter)
        {
            foreach (var proto in _prototypes.EnumeratePrototypes<LanguagePrototype>())
            {
                if (proto.ID == SharedLanguageSystem.UniversalPrototype)
                    continue;

                AddLanguage(result, proto.ID);
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }
        else
        {
            foreach (var lang in spoken)
            {
                if (lang == SharedLanguageSystem.UniversalPrototype)
                    continue;

                AddLanguage(result, lang);
            }
        }

        if (result.Count == 0)
        {
            var current = _language.GetLocalSpeaker()?.CurrentLanguage;
            if (!string.IsNullOrEmpty(current) && current != SharedLanguageSystem.UniversalPrototype)
                AddLanguage(result, current);

            AddLanguage(result, paperLang.Language);
        }

        if (result.Count == 0)
            return result;

        if (result.Exists(entry => entry.Id == paperLang.Language))
            selected = paperLang.Language;
        else if (_language.GetLocalSpeaker()?.CurrentLanguage is { } currentLang &&
                 result.Exists(entry => entry.Id == currentLang))
            selected = currentLang;
        else
            selected = result[0].Id;

        return result;
    }

    /// <summary>
    /// Unknown stretches stay locked. Trailing text in known languages goes into the editor and can be rewritten.
    /// </summary>
    public void GetWriteSplit(EntityUid paper, string fallbackContent, out string lockedMarkup, out string editorText)
    {
        lockedMarkup = string.Empty;
        editorText = fallbackContent;

        if (!TryComp<PaperLanguageComponent>(paper, out var paperLang))
            return;

        var segments = PaperLanguageFormatting.GetEffectiveSegments(paperLang, fallbackContent);
        if (segments.Count == 0)
            return;

        var lockedCount = PaperLanguageFormatting.GetLockedPrefixCount(segments, CanReadPaperLanguage);
        editorText = PaperLanguageFormatting.JoinRange(segments, lockedCount, segments.Count - lockedCount);

        if (lockedCount <= 0)
            return;

        lockedMarkup = FormatSegmentRange(segments, 0, lockedCount, obfuscatedOnly: false)
                       ?? PaperLanguageFormatting.JoinRange(segments, 0, lockedCount);
    }

    private string? FormatSegments(PaperLanguageComponent paperLang, string fallbackContent)
    {
        var segments = PaperLanguageFormatting.GetEffectiveSegments(paperLang, fallbackContent);
        if (segments.Count == 0)
            return null;

        return FormatSegmentRange(segments, 0, segments.Count, obfuscatedOnly: true);
    }

    private string? FormatSegmentRange(
        IReadOnlyList<PaperLanguageSegment> segments,
        int start,
        int count,
        bool obfuscatedOnly)
    {
        var builder = new StringBuilder();
        var changed = false;
        var end = Math.Min(segments.Count, start + count);
        for (var i = start; i < end; i++)
        {
            var segment = segments[i];
            if (builder.Length > 0 && builder[^1] != '\n')
                builder.Append('\n');

            if (CanReadPaperLanguage(segment.Language) || string.IsNullOrWhiteSpace(segment.Text))
            {
                builder.Append(FormattedMessage.EscapeText(segment.Text));
                continue;
            }

            var proto = _language.GetLanguagePrototype(segment.Language);
            var name = proto?.Name ?? segment.Language.Id;
            var header = Loc.GetString("paper-language-obfuscated-header", ("language", name));
            var glyphs = proto == null
                ? segment.Text
                : _language.ObfuscateSpeech(segment.Text, proto);
            builder.Append("[italic]");
            builder.Append(FormattedMessage.EscapeText(header));
            builder.Append("[/italic]\n");
            builder.Append(FormattedMessage.EscapeText(glyphs));
            changed = true;
        }

        if (builder.Length == 0)
            return null;

        if (obfuscatedOnly && !changed)
            return null;

        return builder.ToString();
    }

    private bool CanReadPaperLanguage(ProtoId<LanguagePrototype> language)
    {
        if (_player.LocalEntity is not { } local)
            return false;

        if (HasComp<GhostComponent>(local))
            return true;

        return _language.CanUnderstand(local, language);
    }

    private bool HasLanguageState()
    {
        var speaker = _language.GetLocalSpeaker();
        return speaker != null && (speaker.SpokenLanguages.Count > 0 || speaker.UnderstoodLanguages.Count > 0);
    }

    private void AddLanguage(List<(string Id, string Name)> result, string lang)
    {
        if (result.Exists(entry => entry.Id == lang))
            return;

        var proto = _language.GetLanguagePrototype(lang);
        result.Add((lang, proto?.Name ?? lang));
    }
}
