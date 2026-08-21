using Content.Shared.Language;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Language;

/// <summary>
///     Хранит прогресс изучения языков у игрока: сколько этапов пройдено по каждому языку
///     и когда был пройден последний этап. Прогресс переживает смену книг и перезаходы,
///     пока компонент привязан к игроку.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class N14LanguageLearningComponent : Component
{
    /// <summary>
    ///     Прогресс изучения по языкам: ID языка → количество пройденных этапов.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, int> StagesByLanguage = new();

    /// <summary>
    ///     Момент времени завершения последнего этапа по каждому языку.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, TimeSpan> LastStageTimeByLanguage = new();

    /// <summary>
    ///     Возвращает количество пройденных этапов для указанного языка.
    /// </summary>
    public int GetStages(ProtoId<LanguagePrototype> language)
    {
        return StagesByLanguage.TryGetValue(language, out var stages) ? stages : 0;
    }

    /// <summary>
    ///     Возвращает время завершения последнего этапа для указанного языка (или null, если этапов не было).
    /// </summary>
    public TimeSpan? GetLastStageTime(ProtoId<LanguagePrototype> language)
    {
        return LastStageTimeByLanguage.TryGetValue(language, out var time) ? time : null;
    }

    /// <summary>
    ///     Регистрирует завершённый этап изучения для указанного языка.
    /// </summary>
    public void AddStage(ProtoId<LanguagePrototype> language, TimeSpan time)
    {
        StagesByLanguage[language] = GetStages(language) + 1;
        LastStageTimeByLanguage[language] = time;
    }
}
