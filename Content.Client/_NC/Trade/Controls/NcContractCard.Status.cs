using Content.Shared._NC.Trade;

namespace Content.Client._NC.Trade.Controls;

public sealed partial class NcContractCard
{
    private static ContractObjectiveType GetObjectiveType(ContractClientData data)
    {
        return ContractExecutionKinds.ToObjectiveType(data.ExecutionKind);
    }

    private static bool CanRequestPinpointer(ContractClientData data)
    {
        if (!data.SupportsPinpointer || data.FlowStatus != ContractFlowStatus.InProgress)
            return false;

        return data.ExecutionKind != ContractExecutionKind.InventoryDelivery;
    }

    private static bool IsGhostRoleAwaitingAcceptance(ContractClientData data)
    {
        return GetObjectiveType(data) == ContractObjectiveType.GhostRole &&
            data.FlowStatus == ContractFlowStatus.AwaitingActivation;
    }

    private static bool IsGhostRoleActive(ContractClientData data)
    {
        return GetObjectiveType(data) == ContractObjectiveType.GhostRole &&
            data.FlowStatus == ContractFlowStatus.InProgress;
    }

    private static string BuildGhostRoleStatusText(ContractClientData data)
    {
        if (IsGhostRoleAwaitingAcceptance(data))
            return Loc.GetString("nc-store-contract-ghost-role-waiting-line", ("time", FormatCountdown(data.Runtime.AcceptTimeoutRemainingSeconds)));

        if (IsGhostRoleActive(data))
            return Loc.GetString("nc-store-contract-ghost-role-active-line");

        if (data.FlowStatus == ContractFlowStatus.Failed && !string.IsNullOrWhiteSpace(data.Runtime.FailureReason))
            return data.Runtime.FailureReason;

        return string.Empty;
    }

    private static string BuildActionHintText(ContractClientData data)
    {
        if (data.FlowStatus == ContractFlowStatus.ReadyToTurnIn && !string.IsNullOrWhiteSpace(data.TurnInItem))
            return Loc.GetString("nc-store-contract-action-can-claim-proof");

        return data.FlowStatus switch
        {
            ContractFlowStatus.Available => Loc.GetString("nc-store-contract-action-not-taken"),
            ContractFlowStatus.ReadyToTurnIn => Loc.GetString("nc-store-contract-action-can-claim"),
            ContractFlowStatus.AwaitingActivation => Loc.GetString("nc-store-contract-ghost-role-waiting-line", ("time", FormatCountdown(data.Runtime.AcceptTimeoutRemainingSeconds))),
            ContractFlowStatus.Failed when !string.IsNullOrWhiteSpace(data.Runtime.FailureReason) => data.Runtime.FailureReason,
            _ => IsGhostRoleActive(data)
                ? Loc.GetString("nc-store-contract-ghost-role-active-line")
                : Loc.GetString("nc-store-contract-action-not-done")
        };
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
            ContractObjectiveType.Repair => Loc.GetString("nc-store-contract-type-repair"),
            ContractObjectiveType.GhostRole => Loc.GetString("nc-store-contract-type-ghost-role"),
            _ => Loc.GetString("nc-store-contract-type-delivery")
        };

    private string ObjectiveTypeTooltip(ContractExecutionKind executionKind) =>
        ContractExecutionKinds.ToObjectiveType(executionKind) switch
        {
            ContractObjectiveType.Hunt => Loc.GetString("nc-store-contract-type-hunt-tooltip"),
            ContractObjectiveType.Repair => Loc.GetString("nc-store-contract-type-repair-tooltip"),
            ContractObjectiveType.GhostRole => Loc.GetString("nc-store-contract-type-ghost-role-tooltip"),
            _ => Loc.GetString("nc-store-contract-type-delivery-tooltip")
        };
}
