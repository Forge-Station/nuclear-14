// Forge-Change: paper writing in multiple languages
using Content.Server.Language;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Language;
using Content.Shared.Language.Components;
using Content.Shared.Language.Systems;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;
using static Content.Shared.Paper.SharedPaperComponent;

namespace Content.Server.Paper;

public sealed class PaperLanguageSystem : EntitySystem
{
    [Dependency] private readonly LanguageSystem _language = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaperLanguageComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, PaperLanguageComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var hasWriting = TryComp<PaperComponent>(uid, out var paper) && !string.IsNullOrWhiteSpace(paper.Content);
        if (!hasWriting)
            return;

        var names = new List<string>();
        var seen = new HashSet<string>();
        foreach (var segment in PaperLanguageFormatting.GetEffectiveSegments(component, paper?.Content))
        {
            if (!seen.Add(segment.Language.Id))
                continue;

            var proto = _language.GetLanguagePrototype(segment.Language);
            names.Add(proto?.Name ?? segment.Language.Id);
        }

        if (names.Count == 0)
            return;

        if (names.Count == 1)
        {
            args.PushMarkup(Loc.GetString("paper-language-examine", ("language", names[0])));
            return;
        }

        args.PushMarkup(Loc.GetString("paper-language-examine-multiple",
            ("languages", string.Join(", ", names))));
    }

    public void EnsureSegments(EntityUid uid, PaperComponent paper)
    {
        var paperLang = EnsureComp<PaperLanguageComponent>(uid);
        if (paperLang.Segments.Count > 0 || string.IsNullOrWhiteSpace(paper.Content))
            return;

        paperLang.Segments.Add(new PaperLanguageSegment
        {
            Text = paper.Content,
            Language = paperLang.Language
        });
        Dirty(uid, paperLang);
    }

    /// <summary>
    /// Keeps stretches the writer cannot read, then writes or rewrites the rest in the chosen language.
    /// </summary>
    public bool TryApplyWriting(EntityUid uid, PaperComponent paperComp, PaperInputTextMessage args, out string content)
    {
        content = paperComp.Content;
        var paperLang = EnsureComp<PaperLanguageComponent>(uid);
        EnsureSegments(uid, paperComp);

        if (!TryResolveWriteLanguage(args, paperLang, out var language))
            return false;

        var lockedCount = PaperLanguageFormatting.GetLockedPrefixCount(
            paperLang.Segments,
            lang => CanUnderstandPaper(args.Actor, lang));

        var prefix = PaperLanguageFormatting.JoinRange(paperLang.Segments, 0, lockedCount);
        content = PaperLanguageFormatting.Combine(prefix, args.Text);
        if (content.Length > paperComp.ContentSize)
            return false;

        while (paperLang.Segments.Count > lockedCount)
            paperLang.Segments.RemoveAt(paperLang.Segments.Count - 1);

        if (!string.IsNullOrWhiteSpace(args.Text))
        {
            paperLang.Segments.Add(new PaperLanguageSegment
            {
                Text = args.Text,
                Language = language
            });
        }

        paperLang.Language = language;
        Dirty(uid, paperLang);
        return true;
    }

    private bool TryResolveWriteLanguage(
        PaperInputTextMessage args,
        PaperLanguageComponent paperLang,
        out ProtoId<LanguagePrototype> language)
    {
        var languageId = args.Language;
        if (string.IsNullOrEmpty(languageId))
            languageId = paperLang.Language;

        language = languageId;
        if (language == SharedLanguageSystem.UniversalPrototype || _language.GetLanguagePrototype(language) == null)
            return false;

        return HasComp<GhostComponent>(args.Actor)
               || TryComp<UniversalLanguageSpeakerComponent>(args.Actor, out var uni) && uni.Enabled
               || _language.CanSpeak(args.Actor, language);
    }

    private bool CanUnderstandPaper(EntityUid user, ProtoId<LanguagePrototype> language)
    {
        if (HasComp<GhostComponent>(user)
            || TryComp<UniversalLanguageSpeakerComponent>(user, out var uni) && uni.Enabled)
            return true;

        return _language.CanUnderstand(user, language);
    }
}
