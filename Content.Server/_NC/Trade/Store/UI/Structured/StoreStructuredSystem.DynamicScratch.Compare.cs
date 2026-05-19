using Content.Shared._NC.Trade;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem
{
    private sealed partial class DynamicScratch
    {
        private static bool DictEquals(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            foreach (var (k, v) in a)
            {
                if (!b.TryGetValue(k, out var bv) || bv != v)
                    return false;
            }

            return true;
        }

        private static bool ListEquals(List<ContractClientData> a, List<ContractClientData> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (!ContractEquals(a[i], b[i]))
                    return false;
            }

            return true;
        }

        private static bool StringListEquals(List<string> a, List<string> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;

            return true;
        }

        private static bool ContractEquals(ContractClientData? a, ContractClientData? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;

            if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal) ||
                !string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !string.Equals(a.Description, b.Description, StringComparison.Ordinal) ||
                !string.Equals(a.OfferPoolId, b.OfferPoolId, StringComparison.Ordinal) ||
                !string.Equals(a.OfferPoolName, b.OfferPoolName, StringComparison.Ordinal) ||
                !string.Equals(a.OfferPoolColor, b.OfferPoolColor, StringComparison.Ordinal) ||
                !string.Equals(a.TargetItem, b.TargetItem, StringComparison.Ordinal) ||
                !string.Equals(a.TurnInItem, b.TurnInItem, StringComparison.Ordinal) ||
                !string.Equals(a.SourceHint, b.SourceHint, StringComparison.Ordinal) ||
                !string.Equals(a.DestinationHint, b.DestinationHint, StringComparison.Ordinal))
                return false;

            if (a.Repeatable != b.Repeatable ||
                a.Taken != b.Taken ||
                a.SupportsPinpointer != b.SupportsPinpointer ||
                a.PartialTurnInAvailable != b.PartialTurnInAvailable ||
                a.ExecutionKind != b.ExecutionKind ||
                a.FlowStatus != b.FlowStatus ||
                a.Completed != b.Completed ||
                a.Required != b.Required ||
                a.Progress != b.Progress ||
                a.MatchMode != b.MatchMode ||
                a.OfferPoolOrder != b.OfferPoolOrder ||
                a.IsRetrievalRoute != b.IsRetrievalRoute ||
                a.RetrievalClaimMode != b.RetrievalClaimMode ||
                a.RetrievalProofIsBearer != b.RetrievalProofIsBearer ||
                a.HuntCompletionMode != b.HuntCompletionMode ||
                a.GhostRoleCompletionMode != b.GhostRoleCompletionMode)
                return false;

            return TargetsEquals(a.Targets, b.Targets) &&
                RewardsEquals(a.Rewards, b.Rewards) &&
                RuntimeEquals(a.Runtime, b.Runtime);
        }

        private static bool RuntimeEquals(ContractRuntimeContextData? a, ContractRuntimeContextData? b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null)
                return false;

            return a.Stage == b.Stage &&
                a.StageGoal == b.StageGoal &&
                a.AcceptTimeoutRemainingSeconds == b.AcceptTimeoutRemainingSeconds &&
                a.GhostRoleSurvivalRemainingSeconds == b.GhostRoleSurvivalRemainingSeconds &&
                a.GhostRolePendingAcceptance == b.GhostRolePendingAcceptance &&
                a.Failed == b.Failed &&
                a.Outcome == b.Outcome &&
                string.Equals(a.FailureReason, b.FailureReason, StringComparison.Ordinal) &&
                string.Equals(a.StatusHint, b.StatusHint, StringComparison.Ordinal);
        }

        private static bool TargetsEquals(List<ContractTargetClientData>? a, List<ContractTargetClientData>? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
            {
                var at = a[i];
                var bt = b[i];
                if (!string.Equals(at.TargetItem, bt.TargetItem, StringComparison.Ordinal) ||
                    at.Required != bt.Required ||
                    at.Progress != bt.Progress ||
                    at.MatchMode != bt.MatchMode)
                    return false;
            }

            return true;
        }

        private static bool RewardsEquals(List<ContractRewardData>? a, List<ContractRewardData>? b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null)
                return false;
            if (a.Count != b.Count)
                return false;

            for (var i = 0; i < a.Count; i++)
            {
                var ar = a[i];
                var br = b[i];
                if (ar.Type != br.Type ||
                    ar.Amount != br.Amount ||
                    !string.Equals(ar.Id, br.Id, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
