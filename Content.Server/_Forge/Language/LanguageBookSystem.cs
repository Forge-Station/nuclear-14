using Content.Server.DoAfter;
using Content.Server.Language;
using Content.Shared._Forge.Language;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Language;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Language;

/// <summary>
///     Серверная логика изучения языков по книгам: запуск этапов, проверка кулдаунов
///     и выдача знания языка игроку после достаточного числа этапов.
/// </summary>
public sealed class LanguageBookSystem : SharedLanguageBookSystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly LanguageSystem _language = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<N14LanguageBookComponent, N14LanguageStudyDoAfterEvent>(OnStudyFinished);
        SubscribeLocalEvent<N14LanguageBookComponent, ExaminedEvent>(OnExamined);
    }

    protected override bool TryStartStudy(Entity<N14LanguageBookComponent> book, EntityUid user)
    {
        var (uid, comp) = book;

        if (HasFinishedBook(user, comp))
        {
            _popup.PopupEntity(Loc.GetString("n14-language-book-already-mastered"), uid, user);
            return false;
        }

        var learning = EnsureComp<N14LanguageLearningComponent>(user);
        SeedUnderstoodProgress(user, learning, comp);

        var stages = learning.GetStages(comp.Language);
        if (stages >= comp.MaxStages)
        {
            _popup.PopupEntity(Loc.GetString("n14-language-book-already-mastered"), uid, user);
            return false;
        }

        if (stages > 0 &&
            learning.GetLastStageTime(comp.Language) is { } last &&
            _timing.CurTime < last + TimeSpan.FromSeconds(comp.StageCooldown))
        {
            var remaining = (int) Math.Ceiling((last + TimeSpan.FromSeconds(comp.StageCooldown) - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("n14-language-book-on-cooldown", ("time", remaining)), uid, user);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(comp.StageTime),
            new N14LanguageStudyDoAfterEvent(), uid, target: uid, used: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true
        };

        return _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnStudyFinished(Entity<N14LanguageBookComponent> book, ref N14LanguageStudyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var user = args.Args.User;
        var comp = book.Comp;
        var learning = EnsureComp<N14LanguageLearningComponent>(user);

        var stages = learning.GetStages(comp.Language) + 1;
        learning.AddStage(comp.Language, _timing.CurTime);
        Dirty(user, learning);

        if (comp.TeachesSpeaking && stages >= comp.StagesToMaster)
        {
            _language.AddLanguage(user, comp.Language, addSpoken: true, addUnderstood: true);
            _popup.PopupEntity(Loc.GetString("n14-language-book-mastered", ("language", LanguageName(comp.Language))),
                book, user);
            return;
        }

        if (stages == comp.StagesToUnderstand)
        {
            _language.AddLanguage(user, comp.Language, addSpoken: false, addUnderstood: true);
            _popup.PopupEntity(Loc.GetString("n14-language-book-understood", ("language", LanguageName(comp.Language))),
                book, user);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("n14-language-book-stage",
                ("stage", stages),
                ("total", comp.MaxStages),
                ("language", LanguageName(comp.Language))),
            book, user);
    }

    private void OnExamined(Entity<N14LanguageBookComponent> book, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var name = LanguageName(book.Comp.Language);
        args.PushMarkup(Loc.GetString("n14-language-book-examine", ("language", name)));

        if (!TryComp<N14LanguageLearningComponent>(args.Examiner, out var learning))
            return;

        var stages = learning.GetStages(book.Comp.Language);
        if (HasFinishedBook(args.Examiner, book.Comp) || stages >= book.Comp.MaxStages)
        {
            args.PushMarkup(Loc.GetString("n14-language-book-examine-done"));
            return;
        }

        if (stages > 0)
        {
            args.PushMarkup(Loc.GetString("n14-language-book-examine-progress",
                ("stage", stages),
                ("total", book.Comp.MaxStages)));
        }
    }

    /// <summary>
    ///     If the reader already intrinsically understands the language, skip the "learn to understand" grind.
    /// </summary>
    private void SeedUnderstoodProgress(EntityUid user, N14LanguageLearningComponent learning, N14LanguageBookComponent book)
    {
        if (!book.TeachesSpeaking)
            return;

        if (!HasIntrinsicUnderstanding(user, book.Language))
            return;

        if (learning.GetStages(book.Language) >= book.StagesToUnderstand)
            return;

        learning.StagesByLanguage[book.Language] = book.StagesToUnderstand;
        Dirty(user, learning);
    }

    private bool HasFinishedBook(EntityUid user, N14LanguageBookComponent book)
    {
        if (book.TeachesSpeaking)
            return HasIntrinsicSpeech(user, book.Language);

        return HasIntrinsicUnderstanding(user, book.Language);
    }

    private bool HasIntrinsicSpeech(EntityUid user, ProtoId<LanguagePrototype> language)
    {
        return TryComp<LanguageKnowledgeComponent>(user, out var knowledge)
               && knowledge.SpokenLanguages.Contains(language);
    }

    private bool HasIntrinsicUnderstanding(EntityUid user, ProtoId<LanguagePrototype> language)
    {
        return TryComp<LanguageKnowledgeComponent>(user, out var knowledge)
               && knowledge.UnderstoodLanguages.Contains(language);
    }

    private string LanguageName(ProtoId<LanguagePrototype> language)
    {
        return _prototypeManager.TryIndex(language, out var proto) ? proto.Name : language;
    }
}
