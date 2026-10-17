using Robust.Shared.Configuration;
namespace Content.Shared.CCVar;

/// <summary>
/// Forge-Change: настройки форковых трейтов и наборов снаряжения.
/// </summary>
/// <remarks>
/// Апстрим эти переменные не знает: у него свои трейты (<c>cost</c>) и свои наборы через
/// <c>loadoutEffectGroup</c>, без очков. Форк считает очки, поэтому переменные вернулись
/// в отдельный файл, чтобы следующий апстрим-мердж их не затёр.
/// </remarks>
public sealed partial class CCVars
{
    /// <summary>
    /// Можно ли выбирать трейты.
    /// </summary>
    public static readonly CVarDef<bool> GameTraitsEnabled =
        CVarDef.Create("game.traits_enabled", true, CVar.REPLICATED);

    /// <summary>
    /// Сколько трейтов можно взять максимум.
    /// </summary>
    public static readonly CVarDef<int> GameTraitsMax =
        CVarDef.Create("game.traits_max", 14, CVar.REPLICATED);

    /// <summary>
    /// Сколько очков трейтов даётся персонажу на старте.
    /// </summary>
    public static readonly CVarDef<int> GameTraitsDefaultPoints =
        CVarDef.Create("game.traits_default_points", 10, CVar.REPLICATED);

    /// <summary>
    /// Наказывать ли за превышение лимита очков трейтов. Нарушения пишутся в лог
    /// независимо от этого значения.
    /// </summary>
    public static readonly CVarDef<bool> TraitsPunishCheaters =
        CVarDef.Create("game.traits_punish_cheaters", false, CVar.REPLICATED);

    /// <summary>
    /// Можно ли выбирать предметы в наборе снаряжения.
    /// </summary>
    public static readonly CVarDef<bool> GameLoadoutsEnabled =
        CVarDef.Create("game.loadouts_enabled", true, CVar.REPLICATED);

    /// <summary>
    /// Сколько очков снаряжения даётся игроку.
    /// </summary>
    public static readonly CVarDef<int> GameLoadoutsPoints =
        CVarDef.Create("game.loadouts_points", 14, CVar.REPLICATED);
}
