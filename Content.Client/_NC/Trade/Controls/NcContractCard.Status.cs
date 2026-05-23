using Content.Shared._NC.Trade;


namespace Content.Client._NC.Trade.Controls;


public sealed partial class NcContractCard
{
    private static ContractObjectiveType GetObjectiveType(ContractClientData data) =>
        ContractExecutionKinds.ToObjectiveType(data.ExecutionKind);

    private static bool CanRequestPinpointer(ContractClientData data)
    {
        if (!data.SupportsPinpointer ||
            data.FlowStatus != ContractFlowStatus.InProgress &&
            data.FlowStatus != ContractFlowStatus.ReadyToTurnIn)
            return false;

        return true;
    }

    private static bool IsGhostRoleAwaitingAcceptance(ContractClientData data) =>
        GetObjectiveType(data) == ContractObjectiveType.GhostRole &&
        data.FlowStatus == ContractFlowStatus.AwaitingActivation;

    private static bool IsGhostRoleActive(ContractClientData data) =>
        GetObjectiveType(data) == ContractObjectiveType.GhostRole &&
        data.FlowStatus == ContractFlowStatus.InProgress;

    private static string BuildGhostRoleModeName(ContractClientData data) =>
        data.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.AliveCuffedTurnIn => Loc.GetString("nc-store-contract-ghost-role-mode-alive"),
            NcGhostRoleCompletionMode.DeadBodyTurnIn => Loc.GetString("nc-store-contract-ghost-role-mode-dead"),
            _ => Loc.GetString("nc-store-contract-ghost-role-mode-unknown")
        };

    private static string BuildGhostRoleModeHint(ContractClientData data) =>
        data.GhostRoleCompletionMode switch
        {
            NcGhostRoleCompletionMode.AliveCuffedTurnIn =>
                Loc.GetString("nc-store-contract-ghost-role-mode-alive-hint"),
            NcGhostRoleCompletionMode.DeadBodyTurnIn => Loc.GetString("nc-store-contract-ghost-role-mode-dead-hint"),
            _ => string.Empty
        };

    private static string BuildHuntModeName(ContractClientData data) =>
        data.HuntCompletionMode switch
        {
            NcHuntCompletionMode.BodyTurnIn => Loc.GetString("nc-store-contract-hunt-mode-body"),
            NcHuntCompletionMode.TrophyTurnIn => Loc.GetString("nc-store-contract-hunt-mode-trophy"),
            _ => Loc.GetString("nc-store-contract-hunt-mode-unknown")
        };

    private static string BuildHuntModeHint(ContractClientData data) =>
        data.HuntCompletionMode switch
        {
            NcHuntCompletionMode.BodyTurnIn => Loc.GetString("nc-store-contract-hunt-mode-body-tooltip"),
            NcHuntCompletionMode.TrophyTurnIn => Loc.GetString("nc-store-contract-hunt-mode-trophy-tooltip"),
            _ => string.Empty
        };

    private static string BuildGhostRoleStatusText(ContractClientData data)
    {
        if (IsGhostRoleAwaitingAcceptance(data))
            return Loc.GetString(
                "nc-store-contract-ghost-role-waiting-line",
                ("time", FormatCountdown(data.Runtime.AcceptTimeoutRemainingSeconds)));

        if (IsGhostRoleActive(data))
        {
            var timer = BuildGhostRoleSurvivalStatusLine(data);
            if (!string.IsNullOrWhiteSpace(data.Runtime.StatusHint))
            {
                return string.IsNullOrWhiteSpace(timer)
                    ? data.Runtime.StatusHint
                    : $"{timer} {data.Runtime.StatusHint}";
            }

            return string.IsNullOrWhiteSpace(timer)
                ? Loc.GetString("nc-store-contract-ghost-role-active-line")
                : timer;
        }

        if (!string.IsNullOrWhiteSpace(data.Runtime.StatusHint))
            return data.Runtime.StatusHint;

        if (data.FlowStatus == ContractFlowStatus.Failed && !string.IsNullOrWhiteSpace(data.Runtime.FailureReason))
            return data.Runtime.FailureReason;

        return string.Empty;
    }

    private static string BuildRouteStatusText(ContractClientData data)
    {
        if (!data.IsRetrievalRoute)
            return string.Empty;

        if (data.FlowStatus == ContractFlowStatus.Failed && !string.IsNullOrWhiteSpace(data.Runtime.FailureReason))
            return data.Runtime.FailureReason;

        var max = CalculateRouteRequiredTotal(data);
        var progress = Math.Clamp(data.Progress, 0, max);

        return data.FlowStatus switch
        {
            ContractFlowStatus.Available => Loc.GetString("nc-store-contract-route-status-available"),
            ContractFlowStatus.InProgress when max > 1 => Loc.GetString(
                "nc-store-contract-route-status-progress",
                ("progress", progress),
                ("max", max)),
            ContractFlowStatus.InProgress => progress > 0
                ? Loc.GetString("nc-store-contract-route-status-delivered")
                : Loc.GetString("nc-store-contract-route-status-find-cargo"),
            ContractFlowStatus.ReadyToTurnIn when data.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof &&
                !string.IsNullOrWhiteSpace(data.TurnInItem) => data.RetrievalProofIsBearer
                    ? Loc.GetString("nc-store-contract-route-status-proof-bearer")
                    : Loc.GetString("nc-store-contract-route-status-proof-return"),
            ContractFlowStatus.ReadyToTurnIn when data.RetrievalClaimMode == NcRetrievalClaimMode.StoreCargo =>
                Loc.GetString("nc-store-contract-route-status-store-cargo-ready"),
            ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-route-status-ready"),
            _ => string.Empty
        };
    }

    private static string BuildHuntStatusText(ContractClientData data)
    {
        if (GetObjectiveType(data) != ContractObjectiveType.Hunt)
            return string.Empty;

        if (data.FlowStatus == ContractFlowStatus.Failed && !string.IsNullOrWhiteSpace(data.Runtime.FailureReason))
            return data.Runtime.FailureReason;

        var max = CalculateRouteRequiredTotal(data);
        var progress = Math.Clamp(data.Progress, 0, max);

        return data.HuntCompletionMode switch
        {
            NcHuntCompletionMode.BodyTurnIn => data.FlowStatus switch
            {
                ContractFlowStatus.Available => Loc.GetString("nc-store-contract-hunt-body-status-available"),
                ContractFlowStatus.InProgress => Loc.GetString(
                    "nc-store-contract-hunt-body-status-progress",
                    ("progress", progress),
                    ("required", max)),
                ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-hunt-body-status-ready"),
                _ => string.Empty
            },

            NcHuntCompletionMode.TrophyTurnIn => data.FlowStatus switch
            {
                ContractFlowStatus.Available => Loc.GetString("nc-store-contract-hunt-trophy-status-available"),
                ContractFlowStatus.InProgress => Loc.GetString(
                    "nc-store-contract-hunt-trophy-status-progress",
                    ("progress", progress),
                    ("required", max)),
                ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-hunt-trophy-status-ready"),
                _ => string.Empty
            },

            _ => string.Empty
        };
    }

    private static string BuildActionHintText(ContractClientData data)
    {
        if (data.IsRetrievalRoute)
        {
            var routeHint = BuildRetrievalRouteActionHintText(data);
            if (!string.IsNullOrWhiteSpace(routeHint))
                return routeHint;
        }

        if (GetObjectiveType(data) == ContractObjectiveType.Hunt)
        {
            var huntHint = BuildHuntActionHintText(data);
            if (!string.IsNullOrWhiteSpace(huntHint))
                return huntHint;
        }

        if (data.FlowStatus == ContractFlowStatus.ReadyToTurnIn && !string.IsNullOrWhiteSpace(data.TurnInItem))
            return Loc.GetString("nc-store-contract-action-can-claim-proof");

        if (GetObjectiveType(data) == ContractObjectiveType.GhostRole)
        {
            var hint = BuildGhostRoleActionHintText(data);
            if (!string.IsNullOrWhiteSpace(hint))
                return hint;
        }

        return data.FlowStatus switch
        {
            ContractFlowStatus.Available => Loc.GetString("nc-store-contract-action-not-taken"),
            ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-action-can-claim"),
            ContractFlowStatus.AwaitingActivation => Loc.GetString(
                "nc-store-contract-ghost-role-waiting-line",
                ("time", FormatCountdown(data.Runtime.AcceptTimeoutRemainingSeconds))),
            ContractFlowStatus.Failed when !string.IsNullOrWhiteSpace(data.Runtime.FailureReason) => data.Runtime
                .FailureReason,
            _ => IsGhostRoleActive(data)
                ? Loc.GetString("nc-store-contract-ghost-role-active-line")
                : Loc.GetString("nc-store-contract-action-not-done")
        };
    }

    private static string BuildHuntActionHintText(ContractClientData data)
    {
        var max = CalculateRouteRequiredTotal(data);
        var progress = Math.Clamp(data.Progress, 0, max);

        return data.HuntCompletionMode switch
        {
            NcHuntCompletionMode.BodyTurnIn => data.FlowStatus switch
            {
                ContractFlowStatus.Available => Loc.GetString("nc-store-contract-hunt-body-action-available"),
                ContractFlowStatus.InProgress => Loc.GetString(
                    "nc-store-contract-hunt-body-action-progress",
                    ("progress", progress),
                    ("required", max)),
                ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-hunt-body-action-ready"),
                _ => string.Empty
            },

            NcHuntCompletionMode.TrophyTurnIn => data.FlowStatus switch
            {
                ContractFlowStatus.Available => Loc.GetString("nc-store-contract-hunt-trophy-action-available"),
                ContractFlowStatus.InProgress => Loc.GetString(
                    "nc-store-contract-hunt-trophy-action-progress",
                    ("progress", progress),
                    ("required", max)),
                ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-hunt-trophy-action-ready"),
                _ => string.Empty
            },

            _ => string.Empty
        };
    }

    private static string BuildGhostRoleSurvivalStatusLine(ContractClientData data) =>
        data.Runtime.GhostRoleSurvivalRemainingSeconds > 0
            ? Loc.GetString(
                "nc-store-contract-ghost-role-survival-line",
                ("time", FormatCountdown(data.Runtime.GhostRoleSurvivalRemainingSeconds)))
            : string.Empty;

    private static string BuildGhostRoleActionHintText(ContractClientData data)
    {
        if (IsGhostRoleAwaitingAcceptance(data))
        {
            return Loc.GetString(
                "nc-store-contract-ghost-role-waiting-action",
                ("time", FormatCountdown(data.Runtime.AcceptTimeoutRemainingSeconds)));
        }

        if (!IsGhostRoleActive(data))
            return string.Empty;

        if (data.Runtime.GhostRoleSurvivalRemainingSeconds > 0)
        {
            return Loc.GetString(
                "nc-store-contract-ghost-role-survival-action",
                ("time", FormatCountdown(data.Runtime.GhostRoleSurvivalRemainingSeconds)));
        }

        return Loc.GetString("nc-store-contract-ghost-role-active-short");
    }

    private static string BuildRetrievalRouteActionHintText(ContractClientData data)
    {
        if (data.FlowStatus == ContractFlowStatus.Failed && !string.IsNullOrWhiteSpace(data.Runtime.FailureReason))
            return data.Runtime.FailureReason;

        var max = CalculateRouteRequiredTotal(data);
        var progress = Math.Clamp(data.Progress, 0, max);

        return data.FlowStatus switch
        {
            ContractFlowStatus.Available => Loc.GetString("nc-store-contract-route-action-available"),
            ContractFlowStatus.InProgress when progress < max => Loc.GetString(
                "nc-store-contract-route-action-progress",
                ("progress", progress),
                ("max", max)),
            ContractFlowStatus.InProgress when data.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof =>
                Loc.GetString("nc-store-contract-route-action-proof-after-delivery"),
            ContractFlowStatus.InProgress => Loc.GetString("nc-store-contract-route-action-wait-confirmation"),
            ContractFlowStatus.ReadyToTurnIn when data.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof &&
                !string.IsNullOrWhiteSpace(data.TurnInItem) => data.RetrievalProofIsBearer
                    ? Loc.GetString("nc-store-contract-route-action-proof-bearer")
                    : Loc.GetString("nc-store-contract-route-action-proof"),
            ContractFlowStatus.ReadyToTurnIn when data.RetrievalClaimMode == NcRetrievalClaimMode.StoreCargo =>
                Loc.GetString("nc-store-contract-route-action-store-cargo-ready"),
            ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-route-action-ready"),
            _ => string.Empty
        };
    }

    private static int CalculateRouteRequiredTotal(ContractClientData data)
    {
        if (data.Targets is { Count: > 0, })
        {
            var sum = 0;
            foreach (var target in data.Targets)
                if (target.Required > 0)
                    sum += target.Required;

            return Math.Max(1, sum);
        }

        return Math.Max(1, data.Required);
    }

    private static string FormatCountdown(int totalSeconds)
    {
        var clamped = Math.Max(0, totalSeconds);
        var span = TimeSpan.FromSeconds(clamped);
        return span.TotalHours >= 1
            ? span.ToString(@"hh\:mm\:ss")
            : span.ToString(@"mm\:ss");
    }

    private string ObjectiveTypeName(ContractExecutionKind executionKind) =>
        ContractExecutionKinds.ToObjectiveType(executionKind) switch
        {
            ContractObjectiveType.Hunt => Loc.GetString("nc-store-contract-type-hunt"),
            ContractObjectiveType.GhostRole => Loc.GetString("nc-store-contract-type-ghost-role"),
            _ => Loc.GetString("nc-store-contract-type-delivery")
        };

    private string ObjectiveTypeTooltip(ContractExecutionKind executionKind) =>
        ContractExecutionKinds.ToObjectiveType(executionKind) switch
        {
            ContractObjectiveType.Hunt => Loc.GetString("nc-store-contract-type-hunt-tooltip"),
            ContractObjectiveType.GhostRole => Loc.GetString("nc-store-contract-type-ghost-role-tooltip"),
            _ => Loc.GetString("nc-store-contract-type-delivery-tooltip")
        };
}
