using Content.Shared._NC.Trade;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._NC.Trade.Controls;

public sealed partial class NcContractCard : PanelContainer
{
    private const float DescriptionHorizontalBudget = 40f;
    private ContractClientData _data;
    private readonly IPrototypeManager _proto;
    private readonly SpriteSystem _sprites;
    private readonly IEntityManager _entMan;
    private int _presentationHash;
    private int _skipCost;
    private string _skipCurrency;
    private int _skipBalance;
    private float _lastDescriptionMaxWidth = -1f;
    private RichTextLabel? _descriptionLabel;
    private const int TargetIconPx = 96;
    private const int RewardIconPx = 40;

    public NcContractCard(ContractClientData data, IPrototypeManager protoMan, SpriteSystem sprites, IEntityManager entMan, int skipCost = 0, string skipCurrency = "", int skipBalance = 0)
    {
        _data = data;
        _proto = protoMan;
        _sprites = sprites;
        _entMan = entMan;
        _skipCost = skipCost;
        _skipCurrency = skipCurrency;
        _skipBalance = skipBalance;
        _presentationHash = ComputePresentationHash(data, skipCost, skipCurrency, skipBalance);

        HorizontalExpand = true;
        Margin = new(4, 0, 4, 8);

        BuildUi();
    }

    public event Action<string>? OnClaim;
    public event Action<string>? OnTake;
    public event Action<string>? OnSkip;
    public event Action<string>? OnRequestPinpointer;

    private void BuildUi()
    {
        var borderColor = DifficultyColor(_data.Difficulty, _data.Completed);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true
        };
        AddChild(row);

        var diffStrip = new PanelContainer
        {
            MinSize = new(4, 0),
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = borderColor },
            Margin = new(0, 0, 6, 0)
        };
        row.AddChild(diffStrip);

        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new(0.06f, 0.06f, 0.07f, 0.98f),
                BorderColor = borderColor,
                BorderThickness = new(2),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
        row.AddChild(panel);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        panel.AddChild(root);

        root.AddChild(BuildHeader(borderColor));

        var descText = BuildPrettyDescription(_data);
        if (!string.IsNullOrWhiteSpace(descText))
        {
            var descLabel = new RichTextLabel
            {
                Margin = new(0, 0, 0, 8),
                HorizontalExpand = true,
                ToolTip = descText
            };
            descLabel.SetMessage(descText, null, Color.FromHex("#C9C9C9"));
            _descriptionLabel = descLabel;

            root.AddChild(
                descLabel);
        }

        var ghostRoleStatus = BuildGhostRoleStatusText(_data);
        if (!string.IsNullOrWhiteSpace(ghostRoleStatus))
        {
            var statusLabel = new Label
            {
                Text = ghostRoleStatus,
                Margin = new(0, 0, 0, 6),
                Modulate = IsGhostRoleAwaitingAcceptance(_data)
                    ? Color.FromHex("#D3B06A")
                    : _data.FlowStatus == ContractFlowStatus.Failed
                        ? Color.FromHex("#D97575")
                        : Color.FromHex("#8DB7E8"),
                HorizontalExpand = true,
                ClipText = true,
                ToolTip = ghostRoleStatus
            };

            root.AddChild(
                statusLabel);
        }

        if ((_data.ExecutionKind is ContractExecutionKind.HuntObjective or ContractExecutionKind.RepairObjective or ContractExecutionKind.GhostRoleObjective) && _data.Runtime.StageGoal > 1)
        {
            var stage = Math.Clamp(_data.Runtime.Stage, 0, _data.Runtime.StageGoal);
            root.AddChild(
                new Label
                {
                    Text = Loc.GetString("nc-store-contract-runtime-stage", ("stage", stage), ("goal", _data.Runtime.StageGoal)),
                    Margin = new(0, 0, 0, 6),
                    Modulate = Color.FromHex("#8DB7E8")
                });
        }

        root.AddChild(
            new Label
            {
                Text = Loc.GetString("nc-store-contract-goals-header"),
                Margin = new(0, 0, 0, 2),
                Modulate = Color.FromHex("#8A8A8A")
            });

        if (_data.Targets is { Count: > 0 })
        {
            foreach (var t in _data.Targets)
                root.AddChild(BuildTargetRow(t.TargetItem, t.Required));
        }
        else
        {
            root.AddChild(BuildTargetRow(_data.TargetItem, _data.Required));
        }

        var turnInNote = BuildTurnInNoteText(_data);
        if (!string.IsNullOrWhiteSpace(turnInNote))
        {
            root.AddChild(
                new Label
                {
                    Text = turnInNote,
                    Margin = new(0, 6, 0, 2),
                    Modulate = Color.FromHex("#A8A8A8"),
                    HorizontalExpand = true,
                    ClipText = true,
                    ToolTip = turnInNote
                });
        }

        if (ShouldShowTurnInItem(_data))
        {
            root.AddChild(
                new Label
                {
                    Text = Loc.GetString("nc-store-contract-turn-in-header"),
                    Margin = new(0, 6, 0, 2),
                    Modulate = Color.FromHex("#8A8A8A")
                });

            root.AddChild(BuildTargetRow(_data.TurnInItem, 1));
        }

        if (!_data.Completed)
        {
            var max = CalculateRequiredTotal(_data);
            var val = Math.Clamp(_data.Progress, 0, max);

            var progressLabel = new Label
            {
                Text = Loc.GetString("nc-store-contract-progress-line", ("progress", val), ("required", max)),
                Margin = new(0, 6, 0, 2),
                Align = Label.AlignMode.Left,
                HorizontalExpand = true,
                ClipText = true
            };
            progressLabel.StyleClasses.Add("LabelSubText");
            root.AddChild(progressLabel);

            root.AddChild(
                new ProgressBar
                {
                    MinValue = 0,
                    MaxValue = max,
                    Value = val,
                    HorizontalExpand = true,
                    MinSize = new(0, 10),
                    Margin = new(0, 0, 0, 4)
                });
        }

        root.AddChild(BuildBottom());
    }

    private Control BuildHeader(Color borderColor)
    {
        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new(0, 0, 0, 4)
        };

        var titleRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new(0, 0, 0, 3)
        };
        header.AddChild(titleRow);

        var titleLabel = new Label
        {
            Text = BuildPrettyTitle(_data),
            Margin = new(0, 0, 4, 0),
            HorizontalExpand = true
        };
        titleLabel.StyleClasses.Add("LabelHeading");
        titleLabel.HorizontalExpand = true;
        titleLabel.ClipText = true;
        titleLabel.ToolTip = BuildPrettyTitle(_data);
        titleRow.AddChild(titleLabel);

        var badgesRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true
        };
        header.AddChild(badgesRow);

        var objectiveTypeText = ObjectiveTypeName(_data.ExecutionKind);
        var objectiveTypeTip = ObjectiveTypeTooltip(_data.ExecutionKind);
        badgesRow.AddChild(
            BuildBadge(
                objectiveTypeText,
                objectiveTypeTip,
                Color.FromHex("#202630"),
                Color.FromHex("#5B708D")));

        if (!_data.Repeatable)
        {
            var tip = Loc.GetString("nc-store-contract-badge-single-tooltip");
            badgesRow.AddChild(
                BuildBadge(
                    Loc.GetString("nc-store-contract-badge-single"),
                    tip,
                    new(0.12f, 0.12f, 0.14f),
                    new(0f, 0f, 0f, 0.7f)));
        }

        if (_data.FlowStatus is ContractFlowStatus.AwaitingActivation or ContractFlowStatus.InProgress)
        {
            badgesRow.AddChild(
                BuildBadge(
                    Loc.GetString("nc-store-contract-badge-taken"),
                    Loc.GetString("nc-store-contract-badge-taken-tooltip"),
                    Color.FromHex("#1F2E45"),
                    Color.FromHex("#5E88C9")));

            if (IsGhostRoleAwaitingAcceptance(_data))
            {
                badgesRow.AddChild(
                    BuildBadge(
                        Loc.GetString("nc-store-contract-badge-awaiting-ghost-role"),
                        Loc.GetString("nc-store-contract-badge-awaiting-ghost-role-tooltip"),
                        Color.FromHex("#3A2B12"),
                        Color.FromHex("#C99A3A")));
            }
            else if (IsGhostRoleActive(_data))
            {
                badgesRow.AddChild(
                    BuildBadge(
                        Loc.GetString("nc-store-contract-badge-ghost-role-active"),
                        Loc.GetString("nc-store-contract-badge-ghost-role-active-tooltip"),
                        Color.FromHex("#1C3148"),
                        Color.FromHex("#6EA7E8")));
            }
        }

        if (_data.FlowStatus == ContractFlowStatus.ReadyToTurnIn)
        {
            badgesRow.AddChild(
                BuildBadge(
                    Loc.GetString("nc-store-contract-badge-completed"),
                    Loc.GetString("nc-store-contract-badge-completed-tooltip"),
                    Color.FromHex("#1E3A1E"),
                    Color.FromHex("#4CAF50")));
        }

        return header;
    }

    private static PanelContainer BuildBadge(string text, string? tooltip, Color bg, Color border)
    {
        var badge = new PanelContainer
        {
            VerticalAlignment = VAlignment.Center,
            Margin = new(0, 1, 6, 0),
            MouseFilter = MouseFilterMode.Stop,
            ToolTip = tooltip,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = bg,
                BorderColor = border,
                BorderThickness = new(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2
            }
        };

        var badgeText = new Label
        {
            Text = text,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
            ToolTip = tooltip
        };
        badgeText.StyleClasses.Add("LabelSubText");

        badge.AddChild(badgeText);
        return badge;
    }

    private Control BuildBottom()
    {
        var bottomWrap = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new(0, 6, 0, 0)
        };

        var rewardsPanel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new(0.05f, 0.05f, 0.06f, 0.6f),
                BorderColor = new(0f, 0f, 0f, 0.55f),
                BorderThickness = new(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };

        var rewardsCol = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        rewardsPanel.AddChild(rewardsCol);

        var rewardsHeader = new Label
        {
            Text = Loc.GetString("nc-store-contract-reward-header"),
            Margin = new(0, 0, 0, 3)
        };
        rewardsHeader.StyleClasses.Add("LabelHeading");
        rewardsCol.AddChild(rewardsHeader);

        PopulateRewards(rewardsCol, _data.Rewards);
        bottomWrap.AddChild(rewardsPanel);

        var actionPanel = new PanelContainer
        {
            HorizontalExpand = true,
            Margin = new(0, 6, 0, 0),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = new(0.07f, 0.07f, 0.08f, 0.72f),
                BorderColor = new(0f, 0f, 0f, 0.45f),
                BorderThickness = new(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6
            }
        };
        bottomWrap.AddChild(actionPanel);

        var actionCol = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true
        };
        actionPanel.AddChild(actionCol);

        var canTake = _data.FlowStatus == ContractFlowStatus.Available;
        var canClaim = _data.FlowStatus == ContractFlowStatus.ReadyToTurnIn;
        var canRequestPinpointer = CanRequestPinpointer(_data);

        var actionHint = new Label
        {
            Text = BuildActionHintText(_data),
            Margin = new(0, 0, 0, 4),
            Align = Label.AlignMode.Left,
            HorizontalExpand = true,
            ClipText = true,
            ToolTip = BuildActionHintText(_data)
        };
        actionHint.StyleClasses.Add("LabelSubText");
        actionCol.AddChild(actionHint);

        var btn = new Button
        {
            Text = canTake
                ? Loc.GetString("nc-store-contract-action-take")
                : canClaim
                    ? Loc.GetString("nc-store-contract-action-claim")
                    : Loc.GetString(
                        "nc-store-contract-action-claim-progress",
                        ("progress", _data.Progress),
                        ("required", CalculateRequiredTotal(_data))),
            Disabled = !(canTake || canClaim),
            HorizontalExpand = true,
            MinSize = new(0, 32)
        };

        if (canTake)
            btn.Modulate = Color.FromHex("#3F83F8");
        else if (canClaim)
            btn.Modulate = Color.FromHex("#4CAF50");

        btn.ToolTip = canTake
            ? Loc.GetString("nc-store-contract-take-tooltip")
            : canClaim
                ? !_data.Repeatable
                    ? Loc.GetString("nc-store-contract-claim-tooltip-single")
                    : Loc.GetString("nc-store-contract-claim-tooltip-repeatable")
                : Loc.GetString("nc-store-contract-claim-tooltip-not-done");

        btn.OnPressed += _ =>
        {
            if (canTake)
            {
                OnTake?.Invoke(_data.Id);
                return;
            }

            if (!canClaim)
                return;

            OnClaim?.Invoke(_data.Id);
        };

        actionCol.AddChild(btn);

        BoxContainer? secondaryButtonsRow = null;

        if (canRequestPinpointer)
        {
            secondaryButtonsRow ??= new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new(0, 6, 0, 0)
            };

            var pointerBtn = new Button
            {
                Text = Loc.GetString("nc-store-contract-action-pinpointer"),
                HorizontalExpand = true,
                MinSize = new(0, 28),
                ToolTip = Loc.GetString("nc-store-contract-action-pinpointer-tooltip")
            };

            pointerBtn.OnPressed += _ => OnRequestPinpointer?.Invoke(_data.Id);
            secondaryButtonsRow.AddChild(pointerBtn);
        }

        if (_skipCost > 0 && !string.IsNullOrWhiteSpace(_skipCurrency))
        {
            secondaryButtonsRow ??= new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new(0, 6, 0, 0)
            };

            var skipCurrencyName = CurrencyName(_skipCurrency);
            var canSkip = _data.FlowStatus == ContractFlowStatus.Available && _skipBalance >= _skipCost;
            var skipBtn = new Button
            {
                Text = Loc.GetString("nc-store-contract-action-skip", ("cost", _skipCost), ("currency", skipCurrencyName)),
                HorizontalExpand = true,
                MinSize = new(0, 28),
                Margin = canRequestPinpointer ? new Thickness(6, 0, 0, 0) : default,
                Disabled = !canSkip
            };

            skipBtn.Modulate = canSkip
                ? Color.FromHex("#B0B0B0")
                : Color.FromHex("#8A8A8A");

            var baseTip = Loc.GetString("nc-store-contract-skip-tooltip", ("cost", _skipCost), ("currency", skipCurrencyName));
            skipBtn.ToolTip = _data.FlowStatus != ContractFlowStatus.Available
                ? Loc.GetString("nc-store-contract-skip-locked")
                : canSkip
                    ? baseTip
                    : $"{baseTip}\n{Loc.GetString("nc-store-contract-skip-failed")}";

            skipBtn.OnPressed += _ =>
            {
                if (!canSkip)
                    return;

                OnSkip?.Invoke(_data.Id);
            };

            secondaryButtonsRow.AddChild(skipBtn);
        }

        if (secondaryButtonsRow != null)
            actionCol.AddChild(secondaryButtonsRow);

        return bottomWrap;
    }

    public void UpdateData(ContractClientData data, int skipCost, string skipCurrency, int skipBalance)
    {
        var presentationHash = ComputePresentationHash(data, skipCost, skipCurrency, skipBalance);
        if (_presentationHash == presentationHash)
            return;

        _presentationHash = presentationHash;
        _data = data;
        _skipCost = skipCost;
        _skipCurrency = skipCurrency;
        _skipBalance = skipBalance;
        _descriptionLabel = null;
        _lastDescriptionMaxWidth = -1f;

        DisposeAllChildren();
        RemoveAllChildren();
        BuildUi();
        SyncDescriptionWidth(Size.X);
    }

    protected override void Resized()
    {
        base.Resized();
        SyncDescriptionWidth(Size.X);
    }

    private void SyncDescriptionWidth(float candidateWidth)
    {
        if (_descriptionLabel == null || candidateWidth <= 0 || !float.IsFinite(candidateWidth))
            return;

        var maxWidth = System.MathF.Max(0, candidateWidth - DescriptionHorizontalBudget);
        if (System.MathF.Abs(_lastDescriptionMaxWidth - maxWidth) < 0.5f)
            return;

        _lastDescriptionMaxWidth = maxWidth;
        _descriptionLabel.MaxWidth = maxWidth;
    }
}


