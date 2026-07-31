using System.Linq;

namespace Content.Shared._NC.Sponsor;

public sealed class SponsorData
{
    public static readonly Dictionary<string, SponsorLevel> RolesMap = new()
    {
        { "1228412355705307148", SponsorLevel.Level1 }, // Бустер
        { "1388838190009290932", SponsorLevel.Level1 }, // Подмастерье Форжа
        { "1388839804375924736", SponsorLevel.Level2 }, // Оружейник
        { "1388839967475634176", SponsorLevel.Level3 }, // Мастер Кузни
        { "1388840103966933003", SponsorLevel.Level4 }, // Великий Кузнец
        { "1388840314860736512", SponsorLevel.Level5 }, // Архитектор Горна
        { "1388840456921550942", SponsorLevel.Level6 }, // Демиург Форжа

        { "1487709251777331220", SponsorLevel.Level1 }, // заслужил
        { "1226554881398280272", SponsorLevel.Level2 }, // Модератор
        { "1257637477196370000", SponsorLevel.Level3 }, // Ведущий ментор
        { "1257628115988119562", SponsorLevel.Level3 }, // Смотритель Сервера
        { "1227934528442728498", SponsorLevel.Level4 }, // Начкар
        { "1229422799362195577", SponsorLevel.Level4 }, // Старший ментор
        { "1351127483432570910", SponsorLevel.Level4 }, // ГГМ
        { "1228659342668988416", SponsorLevel.Level4 }, // Старший Модер
        { "1381007703425679522", SponsorLevel.Level6 }, // Помощник Рука
        { "1228303275833425992", SponsorLevel.Level6 } // Руководитель Проекта
    };

    public static readonly Dictionary<SponsorLevel, string> SponsorColor = new()
    {
        { SponsorLevel.Level1, "#6bb9f0" },
        { SponsorLevel.Level2, "#8a9eff" },
        { SponsorLevel.Level3, "#6b8e23" },
        { SponsorLevel.Level4, "#bdbe6b" },
        { SponsorLevel.Level5, "#ff9e2c" },
        { SponsorLevel.Level6, "#ffd700" }
    };

    // Display names shown for each sponsor level in the sponsorship window.
    public static readonly Dictionary<SponsorLevel, string> SponsorNames = new()
    {
        { SponsorLevel.Level1, "Подмастерье Форжа" },
        { SponsorLevel.Level2, "Оружейник" },
        { SponsorLevel.Level3, "Мастер Кузни" },
        { SponsorLevel.Level4, "Великий Кузнец" },
        { SponsorLevel.Level5, "Архитектор Горна" },
        { SponsorLevel.Level6, "Демиург Форжа" }
    };

    // Each level can unlock several ghost skins; list as many entity prototype ids as needed.
    public static readonly Dictionary<SponsorLevel, List<string>> SponsorGhost = new()
    {
        { SponsorLevel.Level1, new() { "SponsorGhostRobast" } },
        { SponsorLevel.Level3, new() { "SponsorGhost1", "SponsorGhostMauler", "SponsorGhostGuardian" } },
        { SponsorLevel.Level4, new() { "SponsorGhost2", "SponsorGhostSeraphim", "SponsorGhostGatchi" } },
        { SponsorLevel.Level5, new() { "SponsorGhost3" } }
    };

    public static SponsorLevel ParseRoles(List<string> roles)
    {
        var highestRole = SponsorLevel.None;
        foreach (var role in roles)
        {
            if (RolesMap.ContainsKey(role))
                if ((byte) RolesMap[role] > (byte) highestRole)
                    highestRole = RolesMap[role];
        }

        return highestRole;
    }

    /// <summary>
    ///     Returns the ghost skins (entity prototype id + minimum required level) a sponsor of the
    ///     given level can choose from, taken from <see cref="SponsorGhost"/>. Duplicate entity ids
    ///     are collapsed to their lowest required level.
    /// </summary>
    public static IReadOnlyList<(string EntityId, SponsorLevel RequiredLevel)> GetAvailableGhostSkins(SponsorLevel level)
    {
        var lowestByEntity = new Dictionary<string, SponsorLevel>();
        foreach (var (requiredLevel, entityIds) in SponsorGhost)
        {
            if (level < requiredLevel)
                continue;

            foreach (var entityId in entityIds)
            {
                if (!lowestByEntity.TryGetValue(entityId, out var existing) || requiredLevel < existing)
                    lowestByEntity[entityId] = requiredLevel;
            }
        }

        return lowestByEntity
            .Select(kv => (kv.Key, kv.Value))
            .OrderBy(x => (byte) x.Value)
            .ToList();
    }

    /// <summary>
    ///     Whether a sponsor of <paramref name="playerLevel"/> is allowed to use the given ghost skin.
    ///     An empty skin id is always allowed (default observer).
    /// </summary>
    public static bool IsGhostSkinAllowed(SponsorLevel playerLevel, string? skinId)
    {
        if (string.IsNullOrEmpty(skinId))
            return true;

        foreach (var (requiredLevel, entityIds) in SponsorGhost)
        {
            if (entityIds.Contains(skinId) && playerLevel >= requiredLevel)
                return true;
        }

        return false;
    }
}

public enum SponsorLevel : byte
{
    None = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6
}
