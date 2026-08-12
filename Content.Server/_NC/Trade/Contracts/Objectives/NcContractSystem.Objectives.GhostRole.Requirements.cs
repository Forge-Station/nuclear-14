using System.Linq;
using Content.Server._NC.Sponsor;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Shared.Customization.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _contractGhostRoleConfig = default!;
    [Dependency] private readonly PlayTimeTrackingManager _contractGhostRolePlayTime = default!;
    [Dependency] private readonly PopupSystem _contractGhostRolePopups = default!;
    [Dependency] private readonly IServerPreferencesManager _contractGhostRolePrefs = default!;
    [Dependency] private readonly CharacterRequirementsSystem _contractGhostRoleRequirements = default!;
    [Dependency] private readonly SponsorManager _contractGhostRoleSponsor = default!;

    private void OnContractGhostRoleGetRequirements(
        EntityUid uid,
        NcContractGhostRoleSpawnerComponent comp,
        GhostRoleGetRequirementsEvent args
    )
    {
        if (comp.Requirements.Count == 0)
            return;

        args.Requirements = comp.Requirements;
    }

    private bool CanTakeContractGhostRole(
        ICommonSession player,
        EntityUid spawner,
        NcContractGhostRoleSpawnerComponent spawnerComp,
        GhostRoleComponent? ghostRole,
        bool popupOnFail = true
    )
    {
        var context = new ContractConditionContext(player, spawner, spawnerComp, ghostRole);
        if (TryEvaluateContractCondition(GhostRoleRequirementsCondition, context, out var failure))
            return true;

        if (popupOnFail && !string.IsNullOrWhiteSpace(failure))
            _contractGhostRolePopups.PopupCursor(failure, player, PopupType.MediumCaution);

        return false;
    }

    private string BuildContractGhostRoleRequirementsFailureMessage(List<string> reasons)
    {
        if (reasons.Count == 0)
            return Loc.GetString("nc-contract-ghost-role-requirements-failed");

        var cleanedReasons = reasons
            .Select(FormattedMessage.RemoveMarkupPermissive)
            .ToArray();

        return $"{Loc.GetString("nc-contract-ghost-role-requirements-failed")}\n{string.Join("\n", cleanedReasons)}";
    }
}
