using Content.Shared._NC.Trade;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;


namespace Content.Client._NC.Trade.Controls;


public sealed partial class NcContractCard
{
    private Control BuildTargetRow(
        string? protoId,
        int required,
        PrototypeMatchMode matchMode = PrototypeMatchMode.Exact,
        string? iconProtoId = null
    )
    {
        var isTagTarget = matchMode == PrototypeMatchMode.Tag;
        EntityPrototype? targetProto = null;
        EntityPrototype? iconProto = null;
        NcMatcherPrototype? targetMatcher = null;
        NcItemGroupPrototype? targetGroup = null;
        NcHuntGroupPrototype? targetHuntGroup = null;
        NcTradeTagPrototype? targetTag = null;
        EntityPrototype? matcherFallbackProto = null;
        EntityPrototype? groupIconProto = null;
        EntityPrototype? tagIconProto = null;
        if (!string.IsNullOrWhiteSpace(protoId))
        {
            if (!isTagTarget)
                _proto.TryIndex(protoId, out targetProto);

            if (isTagTarget && _proto.TryIndex<NcTradeTagPrototype>(protoId, out var tagTarget))
            {
                targetTag = tagTarget;
                if (!string.IsNullOrWhiteSpace(tagTarget.Icon))
                    _proto.TryIndex(tagTarget.Icon, out tagIconProto);
            }

            if (targetProto == null && matchMode == PrototypeMatchMode.Matcher)
            {
                if (_proto.TryIndex<NcMatcherPrototype>(protoId, out var matcher))
                {
                    targetMatcher = matcher;
                    if (matcher.Items.Count > 0)
                        _proto.TryIndex(matcher.Items[0], out matcherFallbackProto);
                }
                else if (_proto.TryIndex<NcItemGroupPrototype>(protoId, out var group))
                {
                    targetGroup = group;
                    if (!string.IsNullOrWhiteSpace(group.Icon))
                        _proto.TryIndex(group.Icon, out groupIconProto);

                    if (groupIconProto == null && group.Prototypes.Count > 0)
                        _proto.TryIndex(group.Prototypes[0], out groupIconProto);
                }
                else if (_proto.TryIndex<NcHuntGroupPrototype>(protoId, out var huntGroup))
                {
                    targetHuntGroup = huntGroup;
                    if (!string.IsNullOrWhiteSpace(huntGroup.Icon))
                        _proto.TryIndex(huntGroup.Icon, out groupIconProto);

                    if (groupIconProto == null && huntGroup.Prototypes.Count > 0)
                        _proto.TryIndex(huntGroup.Prototypes[0], out groupIconProto);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(iconProtoId))
            _proto.TryIndex(iconProtoId, out iconProto);

        var targetRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new(0, 0, 0, 1),
            MouseFilter = MouseFilterMode.Stop,
            HorizontalExpand = true
        };

        var tooltip = targetProto != null
            ? BuildProtoTooltip(targetProto)
            : targetMatcher != null
                ? BuildMatcherTooltip(targetMatcher)
                : targetGroup != null
                    ? BuildItemGroupTooltip(targetGroup)
                    : targetTag != null
                        ? BuildTradeTagTooltip(targetTag)
                        : BuildHuntGroupTooltip(targetHuntGroup);
        if (!string.IsNullOrWhiteSpace(tooltip))
            targetRow.ToolTip = tooltip;

        if (iconProto != null)
            AddPrototypeIcon(targetRow, iconProto.ID);
        else if (targetProto != null)
            AddPrototypeIcon(targetRow, targetProto.ID);
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
                AddPrototypeIcon(targetRow, matcherFallbackProto.ID);
        }
        else if ((targetGroup != null || targetHuntGroup != null) && groupIconProto != null)
            AddPrototypeIcon(targetRow, groupIconProto.ID);
        else if (targetTag != null)
        {
            if (targetTag.Sprite != null && _sprites.Frame0(targetTag.Sprite) is { } tagTexture)
            {
                var texture = new TextureRect
                {
                    Texture = tagTexture,
                    MinSize = new(TargetIconPx, TargetIconPx),
                    MaxSize = new(TargetIconPx, TargetIconPx),
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    Margin = new(0, 0, 8, 0),
                    MouseFilter = MouseFilterMode.Ignore
                };
                targetRow.AddChild(texture);
            }
            else if (tagIconProto != null)
                AddPrototypeIcon(targetRow, tagIconProto.ID);
        }
        else if (!isTagTarget && targetGroup == null && targetHuntGroup == null && !string.IsNullOrWhiteSpace(protoId))
            AddPrototypeIcon(targetRow, protoId);

        var targetName = targetProto?.Name;
        if (string.IsNullOrWhiteSpace(targetName))
            targetName = targetMatcher?.Name;
        if (string.IsNullOrWhiteSpace(targetName))
            targetName = targetGroup?.Name;
        if (string.IsNullOrWhiteSpace(targetName))
            targetName = targetHuntGroup?.Name;
        if (string.IsNullOrWhiteSpace(targetName))
            targetName = targetTag?.Name;
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

    private void AddPrototypeIcon(BoxContainer targetRow, string protoId)
    {
        var view = new EntityPrototypeView
        {
            MinSize = new(TargetIconPx, TargetIconPx),
            MaxSize = new(TargetIconPx, TargetIconPx),
            Margin = new(0, 0, 8, 0),
            MouseFilter = MouseFilterMode.Ignore
        };
        view.SetPrototype(protoId);
        NcUiIconFit.Fit(view, _sprites, protoId, TargetIconPx, 4);
        targetRow.AddChild(view);
    }

    private void PopulateRewards(BoxContainer rewardsCol, List<ContractRewardData>? rewards)
    {
        if (rewards is not { Count: > 0, })
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
                rewardsCol.AddChild(new() { MinSize = new(0, 4), });

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
        NcUiIconFit.Fit(view, _sprites, id, RewardIconPx, 0, 1.25f, 1);
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
