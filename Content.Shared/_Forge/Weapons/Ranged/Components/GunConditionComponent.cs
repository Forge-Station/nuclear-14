using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;


namespace Content.Shared._Forge.Weapons.Ranged.Components;


[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunConditionComponent : Component
{
    #region Состояние

    /// <summary>
    ///     Максимальная прочность оружия.
    /// </summary>
    [DataField]
    public float MaxCondition = 100f;

    /// <summary>
    ///     Текущая прочность оружия.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Condition = 100f;

    /// <summary>
    ///     При этом значении прочности или ниже оружие не может стрелять.
    /// </summary>
    [DataField]
    public float BrokenThreshold;

    #endregion

    #region Пороги износа

    /// <summary>
    ///     Порог состояния "изношено" для отображения (Examine).
    /// </summary>
    [DataField]
    public float WornThreshold = 70f;

    /// <summary>
    ///     Порог состояния "повреждено" для отображения (Examine).
    /// </summary>
    [DataField]
    public float DamagedThreshold = 40f;

    /// <summary>
    ///     Порог состояния "критическое" для отображения (Examine).
    /// </summary>
    [DataField]
    public float CriticalThreshold = 15f;

    #endregion

    #region Заклинивание

    /// <summary>
    ///     Процент прочности, начиная с которого вообще может появляться клин.
    ///     Выше этого значения клин не случается.
    /// </summary>
    [DataField]
    public float JamStart = 70f;

    /// <summary>
    ///     Процент прочности, на котором шанс клина достигает <see cref="JamChanceMax"/>.
    /// </summary>
    [DataField]
    public float JamPeak = 15f;

    /// <summary>
    ///     Минимальный шанс клина (на пороге <see cref="JamStart"/>).
    /// </summary>
    [DataField]
    public float JamChanceMin = 0f;

    /// <summary>
    ///     Максимальный шанс клина (на пороге <see cref="JamPeak"/> и ниже).
    /// </summary>
    [DataField]
    public float JamChanceMax = 0.30f;

    /// <summary>
    ///     Форма роста шанса клина между JamStart и JamPeak.
    ///     1.0 = линейно, >1.0 = растёт медленнее в начале, <1.0 = быстрее в начале.
    /// </summary>
    [DataField]
    public float JamCurve = 1f;

    /// <summary>
    ///     Флаг активного клина: если true, оружие не стреляет.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Jammed;

    #endregion

    #region Износ и ремонт

    /// <summary>
    ///     Потеря прочности за один выстрел.
    /// </summary>
    [DataField]
    public float WearPerShot = 0.45f;

    /// <summary>
    ///     Сколько прочности восстанавливается за одно обслуживание.
    /// </summary>
    [DataField]
    public float RepairAmount = 35f;

    /// <summary>
    ///     Время обслуживания оружия (DoAfter), в секундах.
    /// </summary>
    [DataField]
    public float RepairTime = 2.5f;

    /// <summary>
    ///     Время устранения клина пустой рукой (DoAfter), в секундах.
    /// </summary>
    [DataField]
    public float UnjamTime = 1.25f;

    /// <summary>
    ///     Требуемое качество инструмента для обслуживания оружия.
    /// </summary>
    [DataField]
    public ProtoId<ToolQualityPrototype> RepairToolQuality = "N14GunMaintenance";

    #endregion
}

[RegisterComponent]
public sealed partial class GunConditionRepairToolComponent : Component
{
    /// <summary>
    ///     Сколько успешных обслуживаний может выполнить инструмент.
    ///     -1 означает бесконечное число использований.
    /// </summary>
    [DataField]
    public int Uses = 1;
}
