// #Forge-Change
using Content.Server._NC.Sponsor;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Preferences.Managers;
using Content.Shared._RMC14.Roles.Ranks;
using Content.Shared.Customization.Systems;
using Content.Shared.Players;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._RMC14.Roles.Ranks;

public sealed class RankSystem : SharedRankSystem
{
    [Dependency] private readonly CharacterRequirementsSystem _requirements = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SponsorManager _sponsor = default!;
    [Dependency] private readonly IServerPreferencesManager _preferences = default!;

    // Deliberately retained across disconnects and round restarts, but never persisted to disk.
    private readonly Dictionary<NetUserId, Dictionary<string, string>> _rankPreferences = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeNetworkEvent<RequestRankPreferencesEvent>(OnRequestPreferences);
        SubscribeNetworkEvent<SetRankPreferenceEvent>(OnSetPreference);
    }

    private void OnRequestPreferences(RequestRankPreferencesEvent args, EntitySessionEventArgs session)
    {
        SendPreferences(session.SenderSession);
    }

    private void SendPreferences(ICommonSession session)
    {
        var choices = _rankPreferences.GetValueOrDefault(session.UserId);
        RaiseNetworkEvent(new RankPreferencesEvent(choices == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(choices)), session);
    }

    private void OnSetPreference(SetRankPreferenceEvent args, EntitySessionEventArgs session)
    {
        var player = session.SenderSession;
        if (!_prototypes.TryIndex<JobPrototype>(args.Job, out var job) || job.Ranks == null)
            return;

        if (args.Rank == null)
        {
            if (_rankPreferences.TryGetValue(player.UserId, out var choices))
            {
                choices.Remove(job.ID);
                if (choices.Count == 0)
                    _rankPreferences.Remove(player.UserId);
            }
        }
        else
        {
            var profile = _preferences.GetPreferencesOrNull(player.UserId)?.SelectedCharacter as HumanoidCharacterProfile;
            foreach (var assignment in job.Ranks)
            {
                if (assignment.Rank.Id != args.Rank || profile == null ||
                    !_playTime.TryGetTrackerTimes(player, out var playTimes) ||
                    !IsRankAvailable(assignment, job, profile, playTimes, player.ContentData()?.Whitelisted ?? false))
                    continue;

                if (!_rankPreferences.TryGetValue(player.UserId, out var choices))
                {
                    choices = new Dictionary<string, string>();
                    _rankPreferences[player.UserId] = choices;
                }

                choices[job.ID] = assignment.Rank.Id;
                break;
            }
        }

        // Also acknowledges rejected requests so the selector returns to the server's value.
        SendPreferences(player);
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

        if (_rankPreferences.TryGetValue(args.Player.UserId, out var choices) &&
            choices.TryGetValue(job.ID, out var preferred))
        {
            foreach (var assignment in job.Ranks)
            {
                if (assignment.Rank.Id == preferred &&
                    TryAssignRank(idCard.Owner, assignment, job, args.Profile, playTimes, whitelisted))
                    return;
            }
        }

        foreach (var assignment in job.Ranks)
        {
            if (TryAssignRank(idCard.Owner, assignment, job, args.Profile, playTimes, whitelisted))
                return;
        }
    }

    private bool TryAssignRank(
        EntityUid idCard,
        RankAssignment assignment,
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted)
    {
        if (!_prototypes.TryIndex(assignment.Rank, out RankPrototype? rank))
            return false;

        if (!IsRankAvailable(assignment, job, profile, playTimes, whitelisted))
            return false;

        SetRank(idCard, rank);
        return true;
    }

    private bool IsRankAvailable(
        RankAssignment assignment,
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted)
    {
        return _prototypes.TryIndex(assignment.Rank, out RankPrototype? rank) &&
            _requirements.CheckRequirementsValid(
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
                out _);
    }
}
