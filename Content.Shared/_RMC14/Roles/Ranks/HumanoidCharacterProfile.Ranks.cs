// Forge-Change
using Content.Shared._RMC14.Roles.Ranks;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Preferred cosmetic rank for each job. A missing entry means automatic selection.
    /// </summary>
    [DataField]
    private Dictionary<ProtoId<JobPrototype>, ProtoId<RankPrototype>> _rankPreferences = new();

    /// <see cref="_rankPreferences"/>
    public IReadOnlyDictionary<ProtoId<JobPrototype>, ProtoId<RankPrototype>> RankPreferences => _rankPreferences;

    public HumanoidCharacterProfile WithRankPreference(
        ProtoId<JobPrototype> jobId,
        ProtoId<RankPrototype>? rankId)
    {
        var dictionary = new Dictionary<ProtoId<JobPrototype>, ProtoId<RankPrototype>>(_rankPreferences);

        if (rankId == null)
            dictionary.Remove(jobId);
        else
            dictionary[jobId] = rankId.Value;

        return new HumanoidCharacterProfile(this) { _rankPreferences = dictionary };
    }
}
