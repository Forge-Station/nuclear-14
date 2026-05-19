using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.Trade.Controls;

public sealed partial class NcContractCard
{
    private string BuildPrettyTitle(ContractClientData c)
    {
        if (!string.IsNullOrWhiteSpace(c.Name))
            return c.Name.Trim();

        var goal = BuildGoalsInline(c, 2);
        var group = string.IsNullOrWhiteSpace(c.OfferPoolName)
            ? Loc.GetString("nc-store-contract-title-fallback")
            : c.OfferPoolName.Trim();

        return string.IsNullOrWhiteSpace(goal)
            ? group
            : $"{group}: {goal}";
    }

    private string BuildPrettyDescription(ContractClientData c)
    {
        var description = ResolveBaseDescription(c);
        var ghostRoleHint = BuildGhostRoleDescriptionHint(c);
        var huntHint = BuildHuntDescriptionHint(c);
        var routeHint = BuildRouteHintText(c);

        if (!string.IsNullOrWhiteSpace(ghostRoleHint))
            description = string.IsNullOrWhiteSpace(description)
                ? ghostRoleHint
                : $"{description}\n{ghostRoleHint}";

        if (!string.IsNullOrWhiteSpace(huntHint))
            description = string.IsNullOrWhiteSpace(description)
                ? huntHint
                : $"{description}\n{huntHint}";

        if (string.IsNullOrWhiteSpace(routeHint))
            return description;

        if (string.IsNullOrWhiteSpace(description))
            return routeHint;

        return $"{description}\n{routeHint}";
    }

    private string ResolveBaseDescription(ContractClientData c)
    {
        if (!string.IsNullOrWhiteSpace(c.Description))
            return c.Description.Trim();

        var goal = BuildGoalsInline(c, 4);
        if (string.IsNullOrWhiteSpace(goal))
            return Loc.GetString("nc-store-contract-desc-default");

        return Loc.GetString("nc-store-contract-desc-generated", ("goals", goal.Replace(", ", "; ")));
    }

    private static string BuildGhostRoleDescriptionHint(ContractClientData c)
    {
        if (ContractExecutionKinds.ToObjectiveType(c.ExecutionKind) != ContractObjectiveType.GhostRole)
            return string.Empty;

        return Loc.GetString("nc-store-contract-ghost-role-mode-line-short", ("mode", BuildGhostRoleModeName(c)));
    }

    private static string BuildHuntDescriptionHint(ContractClientData c)
    {
        if (ContractExecutionKinds.ToObjectiveType(c.ExecutionKind) != ContractObjectiveType.Hunt)
            return string.Empty;

        return c.HuntCompletionMode switch
        {
            NcHuntCompletionMode.BodyTurnIn => Loc.GetString("nc-store-contract-hunt-mode-body-desc"),
            NcHuntCompletionMode.TrophyTurnIn => Loc.GetString("nc-store-contract-hunt-mode-trophy-desc"),
            _ => string.Empty
        };
    }

    private static string BuildRouteHintText(ContractClientData c)
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(c.SourceHint))
            parts.Add(c.SourceHint.Trim());

        if (!string.IsNullOrWhiteSpace(c.DestinationHint))
            parts.Add(c.DestinationHint.Trim());

        if (c.RetrievalClaimMode == NcRetrievalClaimMode.DestinationProof && c.RetrievalProofIsBearer)
            parts.Add(Loc.GetString("nc-store-contract-route-proof-bearer-note"));

        return parts.Count == 0
            ? string.Empty
            : string.Join("\n", parts);
    }

    private string BuildGoalsInline(ContractClientData c, int maxParts)
    {
        var parts = new List<string>(maxParts);

        if (c.Targets is { Count: > 0 })
        {
            foreach (var t in c.Targets)
            {
                if (parts.Count >= maxParts)
                    break;

                if (t.Required <= 0 || string.IsNullOrWhiteSpace(t.TargetItem))
                    continue;

                var name = ResolveTargetName(t.TargetItem, t.MatchMode);
                parts.Add(Loc.GetString("nc-store-contract-goal-inline", ("item", name), ("count", t.Required)));
            }
        }
        else if (c.Required > 0 && !string.IsNullOrWhiteSpace(c.TargetItem))
        {
            var name = ResolveTargetName(c.TargetItem, PrototypeMatchMode.Exact);
            parts.Add(Loc.GetString("nc-store-contract-goal-inline", ("item", name), ("count", c.Required)));
        }

        return string.Join(", ", parts);
    }

    private static bool ShouldShowTurnInItem(ContractClientData c)
    {
        if (IsHuntBodyTurnIn(c))
            return true;

        return c.FlowStatus == ContractFlowStatus.ReadyToTurnIn && HasDistinctTurnInItem(c);
    }

    private static string BuildTurnInHeaderText(ContractClientData c)
    {
        if (ContractExecutionKinds.ToObjectiveType(c.ExecutionKind) == ContractObjectiveType.Hunt)
        {
            return c.HuntCompletionMode switch
            {
                NcHuntCompletionMode.BodyTurnIn => Loc.GetString("nc-store-contract-hunt-body-turn-in-header"),
                NcHuntCompletionMode.TrophyTurnIn => Loc.GetString("nc-store-contract-hunt-trophy-turn-in-header"),
                _ => Loc.GetString("nc-store-contract-turn-in-header")
            };
        }

        return Loc.GetString("nc-store-contract-turn-in-header");
    }

    private string BuildTurnInNoteText(ContractClientData c)
    {
        if (string.IsNullOrWhiteSpace(c.TurnInItem) || c.FlowStatus == ContractFlowStatus.ReadyToTurnIn)
            return string.Empty;

        if (ContractExecutionKinds.ToObjectiveType(c.ExecutionKind) == ContractObjectiveType.Hunt)
        {
            return c.HuntCompletionMode switch
            {
                NcHuntCompletionMode.BodyTurnIn => Loc.GetString(
                    "nc-store-contract-hunt-body-turn-in-note",
                    ("item", ResolveProtoName(c.TurnInItem))),
                NcHuntCompletionMode.TrophyTurnIn => Loc.GetString(
                    "nc-store-contract-hunt-trophy-turn-in-note",
                    ("item", ResolveProtoName(c.TurnInItem))),
                _ => string.Empty
            };
        }

        if (!HasDistinctTurnInItem(c))
            return string.Empty;

        return Loc.GetString("nc-store-contract-turn-in-note", ("item", ResolveProtoName(c.TurnInItem)));
    }

    private static bool IsHuntBodyTurnIn(ContractClientData c)
    {
        return ContractExecutionKinds.ToObjectiveType(c.ExecutionKind) == ContractObjectiveType.Hunt &&
               c.HuntCompletionMode == NcHuntCompletionMode.BodyTurnIn &&
               !string.IsNullOrWhiteSpace(c.TurnInItem);
    }

    private static bool HasDistinctTurnInItem(ContractClientData c)
    {
        if (string.IsNullOrWhiteSpace(c.TurnInItem))
            return false;

        if (c.Targets is { Count: > 0 })
        {
            for (var i = 0; i < c.Targets.Count; i++)
            {
                var target = c.Targets[i];
                if (target.Required == 1 && string.Equals(target.TargetItem, c.TurnInItem, StringComparison.Ordinal))
                    return false;
            }
        }

        return !(c.Required == 1 && string.Equals(c.TargetItem, c.TurnInItem, StringComparison.Ordinal));
    }

    private int CalculateRequiredTotal(ContractClientData c)
    {
        if (c.Targets is { Count: > 0 })
        {
            var sum = 0;
            foreach (var t in c.Targets)
            {
                if (t.Required > 0)
                    sum += t.Required;
            }

            return Math.Max(1, sum);
        }

        return Math.Max(1, c.Required);
    }

    private string ResolveProtoName(string protoId)
    {
        if (_proto.TryIndex<EntityPrototype>(protoId, out var proto))
            return proto.Name;

        return protoId;
    }

    private string ResolveTargetName(string protoId, PrototypeMatchMode matchMode)
    {
        if (matchMode == PrototypeMatchMode.Matcher)
        {
            if (_proto.TryIndex<NcMatcherPrototype>(protoId, out var matcher) &&
                !string.IsNullOrWhiteSpace(matcher.Name))
                return matcher.Name;

            if (_proto.TryIndex<NcItemGroupPrototype>(protoId, out var group) &&
                !string.IsNullOrWhiteSpace(group.Name))
                return group.Name;

            if (_proto.TryIndex<NcHuntGroupPrototype>(protoId, out var huntGroup) &&
                !string.IsNullOrWhiteSpace(huntGroup.Name))
                return huntGroup.Name;
        }

        return ResolveProtoName(protoId);
    }

    private static string BuildProtoTooltip(EntityPrototype? proto)
    {
        if (proto == null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(proto.Description))
            return Loc.GetString("nc-store-proto-tooltip-name-only", ("name", proto.Name));

        return Loc.GetString("nc-store-proto-tooltip", ("name", proto.Name), ("desc", proto.Description));
    }

    private static string BuildMatcherTooltip(NcMatcherPrototype? matcher)
    {
        if (matcher == null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(matcher.Description))
            return Loc.GetString("nc-store-proto-tooltip-name-only", ("name", matcher.Name));

        return Loc.GetString("nc-store-proto-tooltip", ("name", matcher.Name), ("desc", matcher.Description));
    }

    private static string BuildItemGroupTooltip(NcItemGroupPrototype? group)
    {
        if (group == null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(group.Description))
            return Loc.GetString("nc-store-proto-tooltip-name-only", ("name", group.Name));

        return Loc.GetString("nc-store-proto-tooltip", ("name", group.Name), ("desc", group.Description));
    }

    private static string BuildHuntGroupTooltip(NcHuntGroupPrototype? group)
    {
        if (group == null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(group.Description))
            return Loc.GetString("nc-store-proto-tooltip-name-only", ("name", group.Name));

        return Loc.GetString("nc-store-proto-tooltip", ("name", group.Name), ("desc", group.Description));
    }

    private string CurrencyName(string? currencyId)
    {
        if (string.IsNullOrWhiteSpace(currencyId))
            return string.Empty;

        if (_proto.TryIndex<StackPrototype>(currencyId, out var stackProto) &&
            _proto.TryIndex<EntityPrototype>(stackProto.Spawn, out var currencyEnt))
            return currencyEnt.Name;

        return currencyId;
    }

}
