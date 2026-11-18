using Content.Shared._Forge.ResourceExtractor;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.ResourceExtractor.UI;

public sealed class ResourceExtractorBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private ResourceExtractorWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ResourceExtractorWindow>();
        _window.OnStart += () => SendMessage(new ResourceExtractorStartMessage());
        _window.OnStop += () => SendMessage(new ResourceExtractorStopMessage());
        _window.OnEjectFuel += () => SendMessage(new ResourceExtractorEjectFuelMessage());
        _window.OnOpenOutput += () => SendMessage(new ResourceExtractorOpenOutputMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (_window == null || state is not ResourceExtractorUiState extractorState)
            return;

        _window.UpdateState(extractorState);
    }
}
