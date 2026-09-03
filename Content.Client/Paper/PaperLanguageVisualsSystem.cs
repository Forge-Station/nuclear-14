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

    private string? FormatSegments(PaperLanguageComponent paperLang, string fallbackContent)
    {
        IReadOnlyList<PaperLanguageSegment> segments;
        if (paperLang.Segments.Count > 0)
        {
            segments = paperLang.Segments;
        }
        else if (string.IsNullOrWhiteSpace(fallbackContent))
        {
            return null;
        }
        else
        {
            segments =
            [
                new PaperLanguageSegment
                {
                    Text = fallbackContent,
                    Language = paperLang.Language
                }
            ];
        }

        var builder = new StringBuilder();
        var changed = false;
        foreach (var segment in segments)
        {
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

        return changed ? builder.ToString() : null;
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
