using Content.Server.Roles;
using Content.Shared._NC.Trade;
using Content.Shared.Mind;
using Robust.Shared.Map;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private void AddGhostRoleBriefing(EntityUid mindId, ContractServerData contract)
    {
        var briefing = BuildGhostRoleBriefing(contract);
        if (string.IsNullOrWhiteSpace(briefing))
            return;

        var briefingComp = EnsureComp<RoleBriefingComponent>(mindId);
        if (string.IsNullOrWhiteSpace(briefingComp.Briefing))
        {
            briefingComp.Briefing = briefing;
            return;
        }

        if (!briefingComp.Briefing.Contains(briefing, StringComparison.Ordinal))
            briefingComp.Briefing += "\n" + briefing;
    }

    private string BuildGhostRoleBriefing(ContractServerData contract)
    {
        var config = contract.Config;

        if (config.GhostRoleSurvivalDurationSeconds <= 0)
            return Loc.GetString(
                "nc-store-contract-ghost-role-character-briefing",
                ("contract", contract.Name));

        return Loc.GetString(
            "nc-store-contract-ghost-role-character-briefing-survival",
            ("contract", contract.Name),
            ("time", FormatGhostRoleDurationText(config.GhostRoleSurvivalDurationSeconds)));
    }

    private void TryAddGhostRoleSurvivalObjective(
        (EntityUid Store, string ContractId) key,
        ObjectiveRuntimeState state,
        ContractServerData contract,
        EntityUid mindId,
        MindComponent mind
    )
    {
        var config = contract.Config;
        if (config.GhostRoleSurvivalDurationSeconds <= 0 ||
            state.GhostRoleSurvivalObjective is { } existing && existing != EntityUid.Invalid)
            return;

        var start = state.GhostRoleSurvivalStart ?? _timing.CurTime;
        var deadline = state.GhostRoleSurvivalDeadline ??
            start + TimeSpan.FromSeconds(config.GhostRoleSurvivalDurationSeconds);

        var objective = Spawn("NcContractGhostRoleSurvivalObjective", MapCoordinates.Nullspace);
        _contractMeta.SetEntityName(objective, ResolveGhostRoleSurvivalObjectiveTitle(contract));
        _contractMeta.SetEntityDescription(objective, ResolveGhostRoleSurvivalObjectiveDescription(contract));

        var survival = EnsureComp<NcContractGhostRoleSurvivalObjectiveComponent>(objective);
        survival.Store = key.Store;
        survival.ContractId = key.ContractId;
        survival.StartedAt = start;
        survival.Deadline = deadline;
        survival.Finished = false;
        survival.Succeeded = false;

        _contractMind.AddObjective(mindId, mind, objective);
        state.GhostRoleSurvivalMind = mindId;
        state.GhostRoleSurvivalObjective = objective;
    }

    private string ResolveGhostRoleSurvivalObjectiveTitle(ContractServerData contract)
    {
        if (!string.IsNullOrWhiteSpace(contract.Config.GhostRoleSurvivalObjectiveTitle))
            return ResolveGhostRoleLocaleText(contract.Config.GhostRoleSurvivalObjectiveTitle);

        return Loc.GetString(
            "nc-store-contract-ghost-role-survival-objective-title",
            ("contract", contract.Name));
    }

    private string ResolveGhostRoleSurvivalObjectiveDescription(ContractServerData contract)
    {
        if (!string.IsNullOrWhiteSpace(contract.Config.GhostRoleSurvivalObjectiveDescription))
            return ResolveGhostRoleLocaleText(contract.Config.GhostRoleSurvivalObjectiveDescription);

        return Loc.GetString(
            "nc-store-contract-ghost-role-survival-objective-description",
            ("time", FormatGhostRoleDurationText(contract.Config.GhostRoleSurvivalDurationSeconds)));
    }

    private string ResolveGhostRoleLocaleText(string text) =>
        Loc.TryGetString(text, out var localized)
            ? localized
            : text;

    private string FormatGhostRoleDurationText(int totalSeconds)
    {
        var seconds = Math.Max(1, totalSeconds);
        var span = TimeSpan.FromSeconds(seconds);
        var parts = new List<string>(2);

        if (span.Hours + span.Days * 24 > 0)
        {
            var hours = span.Hours + span.Days * 24;
            parts.Add(Loc.GetString("nc-store-contract-duration-hours", ("count", hours)));
        }

        if (span.Minutes > 0 && parts.Count < 2)
            parts.Add(Loc.GetString("nc-store-contract-duration-minutes", ("count", span.Minutes)));

        if (parts.Count == 0)
            parts.Add(Loc.GetString("nc-store-contract-duration-seconds", ("count", span.Seconds)));

        return string.Join(" ", parts);
    }
}
