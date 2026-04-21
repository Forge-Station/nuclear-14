using Content.Shared._NC.Trade;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.Trade.Controls;

public sealed partial class NcContractCard
{
    private Control BuildTargetRow(string? protoId, int required, PrototypeMatchMode matchMode = PrototypeMatchMode.Exact)
    {
        EntityPrototype? targetProto = null;
        NcMatcherPrototype? targetMatcher = null;
        EntityPrototype? matcherFallbackProto = null;
        if (!string.IsNullOrWhiteSpace(protoId))
        {
            _proto.TryIndex(protoId, out targetProto);

            if (targetProto == null &&
                matchMode == PrototypeMatchMode.Matcher &&
                _proto.TryIndex<NcMatcherPrototype>(protoId, out var matcher))
            {
                targetMatcher = matcher;
                if (matcher.Items.Count > 0)
                    _proto.TryIndex<EntityPrototype>(matcher.Items[0], out matcherFallbackProto);
            }
        }

        var targetRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new(0, 0, 0, 1),
            MouseFilter = MouseFilterMode.Stop,
            HorizontalExpand = true
        };

        var tooltip = targetProto != null
            ? BuildProtoTooltip(targetProto)
            : BuildMatcherTooltip(targetMatcher);
        if (!string.IsNullOrWhiteSpace(tooltip))
            targetRow.ToolTip = tooltip;

        if (targetProto != null)
        {
            var view = new EntityPrototypeView
            {
                MinSize = new(TargetIconPx, TargetIconPx),
                MaxSize = new(TargetIconPx, TargetIconPx),
                Margin = new(0, 0, 8, 0),
                MouseFilter = MouseFilterMode.Ignore
            };
            view.SetPrototype(targetProto.ID);
            NcUiIconFit.Fit(view, _sprites, targetProto.ID, targetPx: TargetIconPx, paddingPx: 4);
            targetRow.AddChild(view);
        }
        else if (targetMatcher != null)
        {
            if (targetMatcher.Sprite != null && _sprites.Frame0(targetMatcher.Sprite) is { } matcherTexture)
            {
                var texture = new TextureRect
                {
                    Texture = matcherTexture,
                    MinSize = new(TargetIconPx, TargetIconPx),
                    MaxSize = new(TargetIconPx, TargetIconPx),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    Margin = new(0, 0, 8, 0),
                    MouseFilter = MouseFilterMode.Ignore
                };
                targetRow.AddChild(texture);
            }
            else if (matcherFallbackProto != null)
            {
                var view = new EntityPrototypeView
                {
                    MinSize = new(TargetIconPx, TargetIconPx),
                    MaxSize = new(TargetIconPx, TargetIconPx),
                    Margin = new(0, 0, 8, 0),
                    MouseFilter = MouseFilterMode.Ignore
                };
                view.SetPrototype(matcherFallbackProto.ID);
                NcUiIconFit.Fit(view, _sprites, matcherFallbackProto.ID, targetPx: TargetIconPx, paddingPx: 4);
                targetRow.AddChild(view);
            }
        }
        else if (!string.IsNullOrWhiteSpace(protoId))
        {
            var view = new EntityPrototypeView
            {
                MinSize = new(TargetIconPx, TargetIconPx),
                MaxSize = new(TargetIconPx, TargetIconPx),
                Margin = new(0, 0, 8, 0),
                MouseFilter = MouseFilterMode.Ignore
            };
            view.SetPrototype(protoId);
            NcUiIconFit.Fit(view, _sprites, protoId, targetPx: TargetIconPx, paddingPx: 4);
            targetRow.AddChild(view);
        }

        var targetName = targetProto?.Name;
        if (string.IsNullOrWhiteSpace(targetName))
            targetName = targetMatcher?.Name;
        if (string.IsNullOrWhiteSpace(targetName))
            targetName = protoId ?? Loc.GetString("nc-store-unknown-item");

        var targetLabel = new Label
        {
            Text = Loc.GetString("nc-store-contract-goal-line", ("item", targetName), ("count", required)),
            MouseFilter = MouseFilterMode.Ignore,
            HorizontalExpand = true,
            ClipText = true,
            VerticalAlignment = VAlignment.Center,
            Modulate = Color.FromHex("#CAC1B1")
        };
        targetLabel.StyleClasses.Add("LabelSubText");
        targetRow.AddChild(targetLabel);

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
        var label = new Label
        {
            Text = Loc.GetString("nc-store-contract-reward-none"),
            Modulate = Color.FromHex("#8E8577")
        };
        label.StyleClasses.Add("LabelSubText");
        return label;
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

        var label = new Label
        {
            Text = string.Join(", ", parts),
            Modulate = Color.FromHex("#D8B160")
        };
        label.StyleClasses.Add("LabelKeyText");
        return label;
    }

    private BoxContainer BuildItemRewardLine(string id, int count)
    {
        _proto.TryIndex<EntityPrototype>(id, out var proto);

        var line = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new(0, 0, 0, 1),
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
            Margin = new(0, 0, 6, 0),
            MouseFilter = MouseFilterMode.Ignore
        };
        view.SetPrototype(id);
        NcUiIconFit.Fit(view, _sprites, id, targetPx: RewardIconPx, paddingPx: 0, mul: 1.25f, variant: 1);
        line.AddChild(view);

        var name = proto?.Name ?? id;
        var rewardLabel = new Label
        {
            Text = Loc.GetString("nc-store-contract-reward-item-line", ("item", name), ("count", count)),
            MouseFilter = MouseFilterMode.Ignore,
            HorizontalExpand = true,
            ClipText = true,
            VerticalAlignment = VAlignment.Center,
            Modulate = Color.FromHex("#BEB5A5")
        };
        rewardLabel.StyleClasses.Add("LabelSubText");
        line.AddChild(rewardLabel);

        return line;
    }
}
