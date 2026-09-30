using Content.Shared._Forge.QuestInstance;
using JetBrains.Annotations;
using Robust.Client.UserInterface;


namespace Content.Client._Forge.QuestInstance;


[UsedImplicitly]
public sealed class QuestBoardBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private QuestBoardWindow? _window;

    public QuestBoardBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<QuestBoardWindow>();
        _window.OpenCentered();

        _window.OnDifficultySelected += difficulty =>
        {
            SendMessage(new QuestBoardSelectDifficultyMessage(difficulty));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is QuestBoardBoundUserInterfaceState s)
            _window?.UpdateState(s);
    }
}

