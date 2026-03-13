using Content.Shared._NC.Trade;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.Trade.Controls;

public sealed partial class NcContractCard
{
    private Control BuildTargetRow(string? protoId, int required)
    {
        EntityPrototype? targetProto = null;
        if (!string.IsNullOrWhiteSpace(protoId))
            _proto.TryIndex(protoId, out targetProto);

        var targetRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new(0, 0, 0, 2),
            MouseFilter = MouseFilterMode.Stop,
            HorizontalExpand = true
        };

        var tooltip = BuildProtoTooltip(targetProto);
        if (!string.IsNullOrWhiteSpace(tooltip))
            targetRow.ToolTip = tooltip;

        if (!string.IsNullOrWhiteSpace(protoId))
        {
            var view = new EntityPrototypeView
            {
                MinSize = new(TargetIconPx, TargetIconPx),
                MaxSize = new(TargetIconPx, TargetIconPx),
                Margin = new(0, 0, 4, 0),
                MouseFilter = MouseFilterMode.Ignore
            };
            view.SetPrototype(protoId);
            NcUiIconFit.Fit(view, _sprites, protoId, targetPx: TargetIconPx, paddingPx: 4);
            targetRow.AddChild(view);
        }

        var targetName = targetProto?.Name ?? protoId ?? Loc.GetString("nc-store-unknown-item");
        targetRow.AddChild(
            new Label
            {
                Text = Loc.GetString("nc-store-contract-goal-line", ("item", targetName), ("count", required)),
                MouseFilter = MouseFilterMode.Ignore,
                HorizontalExpand = true,
                ClipText = true
            });

        return targetRow;
    }

    private void PopulateRewards(BoxContainer rewardsCol, List<ContractRewardData>? rewards)
    {
        if (rewards is not { Count: > 0 })
        {
            rewardsCol.AddChild(BuildEmptyRewardsLabel());
            return;
        }

        var currencyTotals = new Dictionary<string, int>();
        var itemTotals = new Dictionary<string, int>();

        foreach (var r in rewards)
        {
            if (r.Amount <= 0 || string.IsNullOrWhiteSpace(r.Id))
                continue;

            switch (r.Type)
            {
                case StoreRewardType.Currency:
                    if (!currencyTotals.TryAdd(r.Id, r.Amount))
                        currencyTotals[r.Id] += r.Amount;
                    break;

                case StoreRewardType.Item:
                    if (!itemTotals.TryAdd(r.Id, r.Amount))
                        itemTotals[r.Id] += r.Amount;
                    break;
            }
        }

        if (currencyTotals.Count > 0)
            rewardsCol.AddChild(BuildCurrencyRewardsLine(currencyTotals));

        if (itemTotals.Count > 0)
        {
            if (currencyTotals.Count > 0)
                rewardsCol.AddChild(new Control { MinSize = new(0, 4) });

            foreach (var (id, count) in itemTotals)
            {
                if (count <= 0 || string.IsNullOrWhiteSpace(id))
                    continue;

                rewardsCol.AddChild(BuildItemRewardLine(id, count));
            }
        }

        if (currencyTotals.Count == 0 && itemTotals.Count == 0)
            rewardsCol.AddChild(BuildEmptyRewardsLabel());
    }

    private Label BuildEmptyRewardsLabel()
    {
        return new Label
        {
            Text = Loc.GetString("nc-store-contract-reward-none"),
            Modulate = Color.FromHex("#777777")
        };
    }

    private Label BuildCurrencyRewardsLine(Dictionary<string, int> currencyTotals)
    {
        var parts = new List<string>(currencyTotals.Count);
        foreach (var (currencyId, amount) in currencyTotals)
        {
            var name = CurrencyName(currencyId);
            if (string.IsNullOrWhiteSpace(name))
                name = currencyId;

            parts.Add(Loc.GetString("nc-store-currency-format", ("amount", amount), ("currency", name)));
        }

        return new Label
        {
            Text = string.Join(", ", parts),
            Modulate = Color.FromHex("#D4AF37")
        };
    }

    private BoxContainer BuildItemRewardLine(string id, int count)
    {
        _proto.TryIndex<EntityPrototype>(id, out var proto);

        var line = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new(0, 0, 0, 2),
            MouseFilter = MouseFilterMode.Stop,
            HorizontalExpand = true
        };

        var tooltip = BuildProtoTooltip(proto);
        if (!string.IsNullOrWhiteSpace(tooltip))
            line.ToolTip = tooltip;

        var view = new EntityPrototypeView
        {
            MinSize = new(RewardIconPx, RewardIconPx),
            MaxSize = new(RewardIconPx, RewardIconPx),
            Margin = new(0, 0, 4, 0),
            MouseFilter = MouseFilterMode.Ignore
        };
        view.SetPrototype(id);
        NcUiIconFit.Fit(view, _sprites, id, targetPx: RewardIconPx, paddingPx: 0, mul: 1.25f, variant: 1);
        line.AddChild(view);

        var name = proto?.Name ?? id;
        line.AddChild(
            new Label
            {
                Text = Loc.GetString("nc-store-contract-reward-item-line", ("item", name), ("count", count)),
                MouseFilter = MouseFilterMode.Ignore,
                HorizontalExpand = true,
                ClipText = true
            });

        return line;
    }
}
