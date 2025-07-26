using Robust.Shared.Network;

namespace Content.Shared._NC.Sponsors;

public sealed class SponsorData
{
    public static readonly Dictionary<string, SponsorLevel> RolesMap = new()
    {
        { "1388838190009290932", SponsorLevel.Level1 },
        { "1388839804375924736", SponsorLevel.Level2 },
        { "1388839967475634176", SponsorLevel.Level3 },
        { "1388840103966933003", SponsorLevel.Level4 },
        { "1388840314860736512", SponsorLevel.Level5 },
        { "1388840456921550942", SponsorLevel.Level6 }
    };

    public static readonly Dictionary<SponsorLevel, string> SponsorColor = new()
    {
        { SponsorLevel.Level1, "#6bb9f0" },
        { SponsorLevel.Level2, "#8a9eff" },
        { SponsorLevel.Level3, "#bdbe6b" },
        { SponsorLevel.Level4, "#bdbe6b" },
        { SponsorLevel.Level5, "#ff9e2c" },
        { SponsorLevel.Level6, "#ffd700" }
    };

    public static readonly Dictionary<SponsorLevel, string> SponsorGhost = new()
    {
        { SponsorLevel.Level1, "MobObserver" },
        { SponsorLevel.Level2, "MobObserver" },
        { SponsorLevel.Level3, "MobObserver" },
        { SponsorLevel.Level4, "MobObserver" },
        { SponsorLevel.Level5, "MobObserver" },
        { SponsorLevel.Level6, "MobObserver" }
    };

    public static SponsorLevel ParseRoles(List<string> roles)
    {
        var highestRole = SponsorLevel.None;
        foreach (var role in roles)
        {
            if (RolesMap.ContainsKey(role))
                if ((int) RolesMap[role] > (int) highestRole)
                    highestRole = RolesMap[role];
        }

        return highestRole;
    }

    public SponsorData(SponsorLevel level, NetUserId userId)
    {
        Level = level;
        UserId = userId;
    }

    public SponsorLevel Level;
    public NetUserId UserId;
}

public enum SponsorLevel
{
    None = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Level5 = 5,
    Level6 = 6
}
