// Forge-Change
using System.Collections.Generic;

namespace Content.Server.Database;

public partial class Profile
{
    public List<ProfileRankPreference> RankPreferences { get; } = new();
}

public sealed class ProfileRankPreference
{
    public int Id { get; set; }
    public Profile Profile { get; set; } = null!;
    public int ProfileId { get; set; }

    public string JobName { get; set; } = null!;
    public string RankName { get; set; } = null!;
}
