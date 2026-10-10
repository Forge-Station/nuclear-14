// Forge-Change
using Content.Client.UserInterface.Controls;
using Content.Shared._RMC14.Roles.Ranks;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private readonly List<(ProtoId<JobPrototype> JobId, OptionButton Button, List<ProtoId<RankPrototype>?> RankIds)>
        _rankPreferences = new();

    private OptionButton CreateRankOptions(JobPrototype job)
    {
        var options = new RankOptionButton
        {
            Name = "RankOptionsButton",
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(3f, 3f, 0f, 0f),
        };

        var rankIds = new List<ProtoId<RankPrototype>?> { null };
        if (job.Ranks == null || job.Ranks.Count == 0)
        {
            options.Visible = false;
            return options;
        }

        options.AddItem(Loc.GetString("rmc14-humanoid-profile-editor-rank-auto"));

        foreach (var assignment in job.Ranks)
        {
            if (!_prototypeManager.TryIndex(assignment.Rank, out RankPrototype? rank))
                continue;

            var unlocked = _characterRequirementsSystem.CheckRequirementsValid(
                    assignment.Requirements,
                    job,
                    Profile ?? HumanoidCharacterProfile.DefaultWithSpecies(),
                    _requirements.GetRawPlayTimeTrackers(),
                    _requirements.IsWhitelisted(),
                    rank,
                    _entManager,
                    _prototypeManager,
                    _cfgManager,
                    _sponsorMan,
                    out var reasons);

            var requirements = unlocked
                ? null
                : _characterRequirementsSystem.GetRequirementsText(reasons);

            options.AddRankItem(Loc.GetString(rank.Name), requirements);
            rankIds.Add(assignment.Rank);

            if (!unlocked)
            {
                options.SetItemDisabled(options.ItemCount - 1, true);
            }
        }

        options.OnItemSelected += args =>
        {
            options.SelectId(args.Id);
            var selectedRank = rankIds[args.Id];
            Profile = Profile?.WithRankPreference(job.ID, selectedRank);

            foreach (var (jobId, otherOptions, otherRankIds) in _rankPreferences)
            {
                if (jobId == job.ID)
                    otherOptions.Select(otherRankIds.IndexOf(selectedRank));
            }

            SetDirty();
        };

        _rankPreferences.Add((job.ID, options, rankIds));
        return options;
    }

    private void UpdateRankPreferenceControls()
    {
        foreach (var (jobId, options, rankIds) in _rankPreferences)
        {
            ProtoId<RankPrototype>? preferredRank = null;
            if (Profile?.RankPreferences.TryGetValue(jobId, out var rank) == true)
                preferredRank = rank;

            var selectedIndex = rankIds.IndexOf(preferredRank);
            options.Select(selectedIndex < 0 ? 0 : selectedIndex);
        }
    }

    private sealed class RankOptionButton : OptionButton
    {
        private FormattedMessage? _nextTooltip;

        public void AddRankItem(string label, FormattedMessage? tooltip)
        {
            _nextTooltip = tooltip;
            AddItem(label);
            _nextTooltip = null;
        }

        public override void ButtonOverride(Button button)
        {
            base.ButtonOverride(button);

            if (_nextTooltip == null)
                return;

            var message = _nextTooltip;
            button.TooltipSupplier = _ =>
            {
                var tooltip = new Tooltip();
                tooltip.SetMessage(message);
                return tooltip;
            };
        }
    }
}
