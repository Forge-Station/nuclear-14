// #Forge-Change
using Content.Shared._RMC14.Roles.Ranks;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using ClientRankSystem = Content.Client._RMC14.Roles.Ranks.RankSystem;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private readonly List<(JobPrototype Job, RankOptionButton Button)> _rankSelectors = new();

    private OptionButton CreateRankOptions(JobPrototype job)
    {
        var options = new RankOptionButton
        {
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(6f, 3f, 0f, 0f),
            Visible = job.Ranks is { Count: > 0 },
            ToolTip = Loc.GetString("rmc14-rank-choice-lifetime"),
        };

        if (options.Visible)
            _rankSelectors.Add((job, options));

        return options;
    }

    private void UpdateRankSelectors()
    {
        var ranks = _entManager.System<ClientRankSystem>();
        ranks.RequestPreferences();

        foreach (var (job, options) in _rankSelectors)
        {
            options.Clear();
            options.AddItem(Loc.GetString("rmc14-humanoid-profile-editor-rank-auto"));
            var rankIds = new List<string?> { null };
            var preferred = ranks.GetPreference(job.ID);
            var selectedIndex = 0;

            foreach (var assignment in job.Ranks!)
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

                var name = Loc.GetString(rank.Name);
                options.AddRankItem(name, unlocked
                    ? null
                    : _characterRequirementsSystem.GetRequirementsText(reasons));
                rankIds.Add(rank.ID);
                var index = options.ItemCount - 1;
                options.SetItemDisabled(index, !unlocked);
                if (preferred == rank.ID && unlocked)
                    selectedIndex = index;
            }

            options.Select(selectedIndex);
            options.Disabled = options.ItemCount <= 2;

            // Each selector is populated once per RefreshJobs, which also disposes its predecessor.
            options.OnItemSelected += args =>
            {
                options.SelectId(args.Id);
                ranks.SetPreference(job.ID, rankIds[args.Id]);
            };
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
            if (_nextTooltip is not { } message)
                return;

            button.TooltipSupplier = _ =>
            {
                var tooltip = new Tooltip();
                tooltip.SetMessage(message);
                return tooltip;
            };
        }
    }
}
