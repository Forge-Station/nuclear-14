using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcStoreLogicSystem
{
    public bool TryValidateRewardList(
        EntityUid receiver,
        IReadOnlyList<ContractRewardData>? rewards,
        out string reason)
    {
        if (!TryBuildRewardExecutionPlan(rewards, out var plan, out reason))
            return false;

        return TryValidateRewardExecutionPlan(receiver, plan, out reason);
    }

    public bool TryExecuteRewardList(
        EntityUid receiver,
        IReadOnlyList<ContractRewardData>? rewards,
        string context,
        out string reason)
    {
        if (!TryBuildRewardExecutionPlan(rewards, out var plan, out reason))
            return false;

        return TryExecuteRewardExecutionPlan(receiver, plan, context, out reason);
    }

    private bool TryBuildRewardExecutionPlan(
        IReadOnlyList<ContractRewardData>? rewards,
        out NcRewardExecutionPlan plan,
        out string reason)
    {
        plan = new();
        reason = string.Empty;

        if (rewards == null || rewards.Count == 0)
            return true;

        for (var i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];
            if (reward.Amount <= 0 || string.IsNullOrWhiteSpace(reward.Id))
                continue;

            if (reward.Type != StoreRewardType.Currency && reward.Type != StoreRewardType.Item)
            {
                reason = $"Reward #{i} uses unsupported reward type '{reward.Type}'.";
                return false;
            }

            if (!TryAddRewardExecutionEntry(plan, reward.Type, reward.Id, reward.Amount, out reason))
                return false;
        }

        return true;
    }

    private bool TryBuildRewardExecutionPlan(
        BarterReceivePlan receivePlan,
        out NcRewardExecutionPlan plan,
        out string reason)
    {
        plan = new();
        reason = string.Empty;

        if (receivePlan.Entries.Count == 0)
        {
            reason = "Barter receive plan is empty.";
            return false;
        }

        for (var i = 0; i < receivePlan.Entries.Count; i++)
        {
            var entry = receivePlan.Entries[i];
            if (entry.Count <= 0)
            {
                reason = $"Barter receive entry #{i} has invalid count {entry.Count}.";
                return false;
            }

            var hasCurrency = !string.IsNullOrWhiteSpace(entry.Currency);
            var hasPrototype = !string.IsNullOrWhiteSpace(entry.Prototype);
            if (hasCurrency == hasPrototype)
            {
                reason = $"Barter receive entry #{i} must reference exactly one currency or prototype.";
                return false;
            }

            var type = hasCurrency ? StoreRewardType.Currency : StoreRewardType.Item;
            var id = hasCurrency ? entry.Currency : entry.Prototype;
            if (!TryAddRewardExecutionEntry(plan, type, id, entry.Count, out reason))
                return false;
        }

        return plan.Entries.Count > 0;
    }

    private static bool TryAddRewardExecutionEntry(
        NcRewardExecutionPlan plan,
        StoreRewardType type,
        string id,
        int amount,
        out string reason)
    {
        reason = string.Empty;

        if (amount <= 0)
            return true;

        if (string.IsNullOrWhiteSpace(id))
        {
            reason = $"Reward entry of type '{type}' has empty id.";
            return false;
        }

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var existing = plan.Entries[i];
            if (existing.Type != type || !string.Equals(existing.Id, id, StringComparison.Ordinal))
                continue;

            var total = (long) existing.Amount + amount;
            if (total > int.MaxValue)
            {
                reason = $"Reward entry '{id}' amount overflow.";
                return false;
            }

            plan.Entries[i] = existing with { Amount = (int) total };
            return true;
        }

        plan.Entries.Add(new(type, id, amount));
        return true;
    }

    private bool TryValidateRewardExecutionPlan(
        EntityUid receiver,
        NcRewardExecutionPlan plan,
        out string reason)
    {
        reason = string.Empty;

        if (!Exists(receiver))
        {
            reason = $"Reward receiver no longer exists: {ToPrettyString(receiver)}.";
            return false;
        }

        var needsCoordinates = false;
        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var entry = plan.Entries[i];
            if (entry.Amount <= 0)
            {
                reason = $"Reward plan entry #{i} has invalid amount {entry.Amount}.";
                return false;
            }

            switch (entry.Type)
            {
                case StoreRewardType.Currency:
                    if (_protos.HasIndex<StackPrototype>(entry.Id) && CanHandleCurrency(entry.Id))
                        continue;

                    reason = $"Reward plan entry #{i} references missing or unsupported currency stack prototype '{entry.Id}'.";
                    return false;

                case StoreRewardType.Item:
                    if (!_protos.HasIndex<EntityPrototype>(entry.Id))
                    {
                        reason = $"Reward plan entry #{i} references missing item prototype '{entry.Id}'.";
                        return false;
                    }

                    needsCoordinates = true;
                    continue;

                default:
                    reason = $"Reward plan entry #{i} uses unsupported reward type '{entry.Type}'.";
                    return false;
            }
        }

        if (needsCoordinates && !TryComp(receiver, out TransformComponent? _xform))
        {
            reason = $"Reward receiver has no TransformComponent: {ToPrettyString(receiver)}.";
            return false;
        }

        return true;
    }

    private bool TryExecuteRewardExecutionPlan(
        EntityUid receiver,
        NcRewardExecutionPlan plan,
        string context,
        out string reason)
    {
        if (!TryValidateRewardExecutionPlan(receiver, plan, out reason))
            return false;

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var entry = plan.Entries[i];
            if (entry.Type != StoreRewardType.Item)
                continue;

            var spawned = TrySpawnProductUnits(entry.Id, receiver, entry.Amount);
            if (spawned >= entry.Amount)
                continue;

            reason = $"{context}: item reward spawn shortfall for '{entry.Id}': spawned {spawned}/{entry.Amount}.";
            Sawmill.Error($"[NcStore] {reason}");
            return false;
        }

        for (var i = 0; i < plan.Entries.Count; i++)
        {
            var entry = plan.Entries[i];
            if (entry.Type != StoreRewardType.Currency)
                continue;

            if (TryGiveCurrency(receiver, entry.Id, entry.Amount))
                continue;

            reason = $"{context}: failed to give currency '{entry.Id}' x{entry.Amount}.";
            Sawmill.Error($"[NcStore] {reason}");
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private sealed class NcRewardExecutionPlan
    {
        public readonly List<NcRewardExecutionEntry> Entries = new();
    }

    private readonly record struct NcRewardExecutionEntry(StoreRewardType Type, string Id, int Amount);
}
