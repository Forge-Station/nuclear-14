using Content.Shared.Corvax.Forge;
using Robust.Client.UserInterface;

namespace Content.Client.Corvax.Forge;

/// <summary>
///     Client side of the armor-assembly minigame opened on the mannequin.
/// </summary>
public sealed class N14ForgeMiniGameBoundUserInterface : BoundUserInterface
{
    private N14ForgeMiniGameWindow? _window;

    public N14ForgeMiniGameBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<N14ForgeMiniGameWindow>();
        _window.Completed += OnMiniGameCompleted;
        _window.OpenCentered();
    }

    private void OnMiniGameCompleted()
    {
        SendMessage(new N14ForgeMiniGameCompletedMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is N14ForgeMiniGameState mini)
            _window?.Setup(mini.TorsoProto, mini.PartProtos);
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
    }
}