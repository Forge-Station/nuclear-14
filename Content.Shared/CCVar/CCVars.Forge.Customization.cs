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

    /// <summary>
    /// Forge-Change (Frontier): трение космоса, когда инерционные гасители выключены.
    /// </summary>
    public static readonly CVarDef<float> SpaceFrictionStrength =
        CVarDef.Create("shuttle.space_friction_strength", 0.0015f, CVar.REPLICATED);

    /// <summary>
    /// Forge-Change (Frontier): сила торможения, когда гасители удерживают судно на месте.
    /// </summary>
    public static readonly CVarDef<float> AnchorDampeningStrength =
        CVarDef.Create("shuttle.ancho_dampening_strength", 0.5f, CVar.REPLICATED);

    /// <summary>
    /// Forge-Change: как часто ИИ-кремниевые тратят заряд, в секундах.
    /// </summary>
    public static readonly CVarDef<float> SiliconNpcUpdateTime =
        CVarDef.Create("silicon.npcupdatetime", 1.5f, CVar.SERVERONLY);

    /// <summary>
    /// Forge-Change: подставлять в имя сервера текущую карту и режим.
    /// </summary>
    public static readonly CVarDef<bool> UseDynamicHostname =
        CVarDef.Create("game.use_dynamic_hostname", false, CVar.SERVERONLY);

    /// <summary>
    /// Forge-Change: наименьший промежуток между «колеблющимися» событиями станции, в секундах.
    /// </summary>
    public static readonly CVarDef<int> GameEventsOscillatingMinimumTime =
        CVarDef.Create("game.events_oscillating_minimum_time", 40, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Forge-Change: промежуток между «колеблющимися» событиями при единичном хаосе, в секундах.
    /// </summary>
    public static readonly CVarDef<int> GameEventsOscillatingAverageTime =
        CVarDef.Create("game.events_oscillating_average_time", 1200, CVar.SERVERONLY | CVar.ARCHIVE);
}
