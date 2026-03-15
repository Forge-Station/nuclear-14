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

        var diff = DifficultyName(c.Difficulty);
        var goal = BuildGoalsInline(c, 2);

        return string.IsNullOrWhiteSpace(goal)
            ? Loc.GetString("nc-store-contract-title-pretty-nogoal", ("difficulty", diff))
            : Loc.GetString("nc-store-contract-title-pretty", ("difficulty", diff), ("goal", goal));
    }

    private string BuildPrettyDescription(ContractClientData c)
    {
        if (!string.IsNullOrWhiteSpace(c.Description))
            return c.Description.Trim();

        var goal = BuildGoalsInline(c, 4);
        if (string.IsNullOrWhiteSpace(goal))
            return Loc.GetString("nc-store-contract-desc-default");

        return Loc.GetString("nc-store-contract-desc-generated", ("goals", goal.Replace(", ", "; ")));
    }

    private string BuildDisplayDescription(ContractClientData c, int maxChars)
    {
        return TrimToChars(BuildPrettyDescription(c), maxChars);
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

                var name = ResolveProtoName(t.TargetItem);
                parts.Add(Loc.GetString("nc-store-contract-goal-inline", ("item", name), ("count", t.Required)));
            }
        }
        else if (c.Required > 0 && !string.IsNullOrWhiteSpace(c.TargetItem))
        {
            var name = ResolveProtoName(c.TargetItem);
            parts.Add(Loc.GetString("nc-store-contract-goal-inline", ("item", name), ("count", c.Required)));
        }

        return string.Join(", ", parts);
    }

    private static bool ShouldShowTurnInItem(ContractClientData c)
    {
        return c.FlowStatus == ContractFlowStatus.ReadyToTurnIn && HasDistinctTurnInItem(c);
    }

    private string BuildTurnInNoteText(ContractClientData c)
    {
        if (!HasDistinctTurnInItem(c) || c.FlowStatus == ContractFlowStatus.ReadyToTurnIn)
            return string.Empty;

        return Loc.GetString("nc-store-contract-turn-in-note", ("item", ResolveProtoName(c.TurnInItem)));
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

    private static string BuildProtoTooltip(EntityPrototype? proto)
    {
        if (proto == null)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(proto.Description))
            return Loc.GetString("nc-store-proto-tooltip-name-only", ("name", proto.Name));

        return Loc.GetString("nc-store-proto-tooltip", ("name", proto.Name), ("desc", proto.Description));
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

    private Color DifficultyColor(string diff, bool completed)
    {
        var baseColor = diff switch
        {
            "Easy" => Color.FromHex("#4CAF50"),
            "Medium" => Color.FromHex("#FFC107"),
            "Hard" => Color.FromHex("#F44336"),
            _ => Color.FromHex("#9E9E9E")
        };

        return completed ? Brighten(baseColor, 0.7f) : baseColor;
    }

    private string DifficultyName(string diff) =>
        diff switch
        {
            "Easy" => Loc.GetString("nc-store-difficulty-easy"),
            "Medium" => Loc.GetString("nc-store-difficulty-medium"),
            "Hard" => Loc.GetString("nc-store-difficulty-hard"),
            _ => diff
        };

    private static Color Brighten(Color c, float f) =>
        new(MathF.Min(c.R * f, 1f), MathF.Min(c.G * f, 1f), MathF.Min(c.B * f, 1f), c.A);

    private static string TrimToChars(string text, int maxChars)
    {
        if (maxChars <= 0 || text.Length <= maxChars)
            return text;

        return text[..maxChars].TrimEnd() + "...";
    }
}

