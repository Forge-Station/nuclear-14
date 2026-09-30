// Forge-Change
using System.Linq;
using Content.Server._NC.Sponsor;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared._RMC14.Roles.Ranks;
using Content.Shared.Customization.Systems;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._RMC14.Roles.Ranks;

public sealed class RankSystem : SharedRankSystem
{
    [Dependency] private readonly CharacterRequirementsSystem _requirements = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SponsorManager _sponsor = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null ||
            !_prototypes.TryIndex<JobPrototype>(args.JobId, out var job) ||
            job.Ranks == null)
        {
            return;
        }

        if (!_playTime.TryGetTrackerTimes(args.Player, out var playTimes))
        {
            Log.Error($"Playtimes were not ready when assigning a rank to {args.Player}.");
            playTimes = new Dictionary<string, TimeSpan>();
        }

        var whitelisted = args.Player.ContentData()?.Whitelisted ?? false;

        if (!TryGetRankIdCard(args.Mob, out var idCard))
        {
            Log.Warning($"Could not assign a rank to {args.Player}: no ID card was equipped.");
            return;
        }

        if (args.Profile.RankPreferences.TryGetValue(job.ID, out var preferredRank) &&
            TryAssignRank(idCard.Owner, preferredRank, job, args.Profile, playTimes, whitelisted))
        {
            return;
        }

        foreach (var assignment in job.Ranks)
        {
            if (TryAssignRank(idCard.Owner, assignment.Rank, job, args.Profile, playTimes, whitelisted))
                return;
        }
    }

    private bool TryAssignRank(
        EntityUid idCard,
        ProtoId<RankPrototype> rankId,
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted)
    {
        var assignment = job.Ranks?.FirstOrDefault(entry => entry.Rank == rankId);
        if (assignment == null || !_prototypes.TryIndex(rankId, out RankPrototype? rank))
            return false;

        if (!_requirements.CheckRequirementsValid(
                assignment.Requirements,
                job,
                profile,
                playTimes,
                whitelisted,
                rank,
                EntityManager,
                _prototypes,
                _configuration,
                _sponsor,
                out _))
        {
            return false;
        }

        SetRank(idCard, rank);
        return true;
    }
}
