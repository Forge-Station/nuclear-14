using Content.Client._NC.Radio.UI;
using Content.Shared._NC.Radio;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.Silicons.Junkbot;

[UsedImplicitly]
public sealed class JunkbotRadioBoundUserInterface : BoundUserInterface
{
    private HandheldRadioMenu? _menu;
    private HandheldRadioBoundUIState? _lastState;

    public JunkbotRadioBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _lastState = null;
        _menu = new HandheldRadioMenu();
        _menu.OnMicPressed += enabled => SendMessage(new ToggleHandheldRadioMicMessage(enabled));
        _menu.OnSpeakerPressed += enabled => SendMessage(new ToggleHandheldRadioSpeakerMessage(enabled));
        _menu.OnFrequencyChanged += frequency =>
        {
            // Apply the reply even if the server rejects the requested frequency
            // and sends back the same state.
            _lastState = null;
            var selected = int.TryParse(frequency.Trim(), out var parsed) && parsed > 0 ? parsed : -1;
            SendMessage(new SelectHandheldRadioFrequencyMessage(selected));
        };
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not HandheldRadioBoundUIState radioState)
            return;

        // Battery updates resend all of the junkbot's UI states. Reapplying an
        // unchanged radio state would replace the frequency currently being typed.
        if (_lastState is { } previous &&
            previous.Frequency == radioState.Frequency &&
            previous.MicEnabled == radioState.MicEnabled &&
            previous.SpeakerEnabled == radioState.SpeakerEnabled)
            return;

        _lastState = radioState;
        _menu?.Update(radioState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _menu?.Close();
    }
}
