using Content.Shared.Language;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Language;

/// <summary>
///     Книга для изучения языка. При использовании в руке запускается этап изучения (DoAfter),
///     после достаточного числа этапов игрок получает знание языка.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedLanguageBookSystem))]
public sealed partial class N14LanguageBookComponent : Component
{
    /// <summary>
    ///     Язык, который изучается по этой книге.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<LanguagePrototype> Language = "English";

    /// <summary>
    ///     Длительность одного этапа изучения (DoAfter), в секундах.
    /// </summary>
    [DataField]
    public float StageTime = 4f;

    /// <summary>
    ///     Минимальная пауза (в секундах) между завершёнными этапами изучения.
    /// </summary>
    [DataField]
    public float StageCooldown = 10f;

    /// <summary>
    ///     Сколько этапов нужно пройти, чтобы начать понимать язык (но не говорить).
    /// </summary>
    [DataField]
    public int StagesToUnderstand = 5;

    /// <summary>
    ///     Сколько этапов нужно пройти, чтобы свободно говорить и понимать язык.
    /// </summary>
    [DataField]
    public int StagesToMaster = 10;

    /// <summary>
    ///     Разрешает ли книга научить говорить на языке. Если false — даже после всех этапов
    ///     игрок сможет только понимать язык, но не говорить на нём.
    /// </summary>
    [DataField]
    public bool TeachesSpeaking = true;
}
