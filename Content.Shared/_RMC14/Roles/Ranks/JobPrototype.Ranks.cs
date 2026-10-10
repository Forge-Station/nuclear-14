// Forge-Change
using Content.Shared._RMC14.Roles.Ranks;

// Keep rank data separate from the upstream job prototype.
namespace Content.Shared.Roles;

public sealed partial class JobPrototype
{
    /// <summary>
    /// Cosmetic ranks available to this job, ordered from highest to lowest.
    /// The first assignment whose requirements pass is selected on spawn.
    /// </summary>
    [DataField]
    public List<RankAssignment>? Ranks { get; private set; }
}
