using Content.Server.DoAfter;
using Content.Server.Language;
using Content.Shared._Forge.Language;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
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
    }

    protected override bool TryStartStudy(Entity<N14LanguageBookComponent> book, EntityUid user)
    {
        var (uid, comp) = book;

        var learning = EnsureComp<N14LanguageLearningComponent>(user);
        var stages = learning.GetStages(comp.Language);

        // Все этапы уже пройдены — язык полностью изучен.
        var maxStages = comp.TeachesSpeaking ? comp.StagesToMaster : comp.StagesToUnderstand;
        if (stages >= maxStages)
        {
            _popup.PopupEntity(Loc.GetString("n14-language-book-already-mastered"), uid, user);
            return false;
        }

        // Проверяем кулдаун между этапами.
        if (stages > 0 &&
            learning.GetLastStageTime(comp.Language) is { } last &&
            _timing.CurTime < last + TimeSpan.FromSeconds(comp.StageCooldown))
        {
            var remaining = (int) Math.Ceiling((last + TimeSpan.FromSeconds(comp.StageCooldown) - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("n14-language-book-on-cooldown", ("time", remaining)), uid, user);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(comp.StageTime),
            new N14LanguageStudyDoAfterEvent(), uid, target: uid)
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

        // Достигнут порог полного владения: говорить и понимать (если книга это позволяет).
        if (comp.TeachesSpeaking && stages >= comp.StagesToMaster)
        {
            _language.AddLanguage(user, comp.Language, addSpoken: true, addUnderstood: true);
            _popup.PopupEntity(Loc.GetString("n14-language-book-mastered", ("language", LanguageName(comp.Language))),
                book, user);
        }
        // Достигнут порог понимания: понимать, но не говорить.
        else if (stages >= comp.StagesToUnderstand)
        {
            _language.AddLanguage(user, comp.Language, addSpoken: false, addUnderstood: true);
            _popup.PopupEntity(Loc.GetString("n14-language-book-understood", ("language", LanguageName(comp.Language))),
                book, user);
        }
        else
        {
            _popup.PopupEntity(
                Loc.GetString("n14-language-book-stage", ("stage", stages), ("language", LanguageName(comp.Language))),
                book, user);
        }
    }

    private string LanguageName(ProtoId<LanguagePrototype> language)
    {
        return _prototypeManager.TryIndex(language, out var proto) ? proto.Name : language;
    }
}