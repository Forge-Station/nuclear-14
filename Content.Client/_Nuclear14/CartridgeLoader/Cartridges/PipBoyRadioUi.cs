using Content.Client.UserInterface.Fragments;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.Audio.Jukebox;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._Nuclear14.CartridgeLoader.Cartridges;

public sealed partial class PipBoyRadioUi : UIFragment
{
    private PipBoyRadioUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(
        BoundUserInterface userInterface,
        EntityUid? fragmentOwner)
    {
        _fragment = new PipBoyRadioUiFragment();

        var prototypeManager =
            IoCManager.Resolve<IPrototypeManager>();

        _fragment.SetPrototypeManager(prototypeManager);

        _fragment.OnSongSelected += id =>
            Send(
                new PipBoyRadioUiMessageEvent(
                    PipBoyRadioAction.Select,
                    new ProtoId<JukeboxPrototype>(id)),
                userInterface);

        _fragment.OnPlay += () =>
            Send(
                new PipBoyRadioUiMessageEvent(
                    PipBoyRadioAction.Play),
                userInterface);

        _fragment.OnPause += () =>
            Send(
                new PipBoyRadioUiMessageEvent(
                    PipBoyRadioAction.Pause),
                userInterface);

        _fragment.OnStop += () =>
            Send(
                new PipBoyRadioUiMessageEvent(
                    PipBoyRadioAction.Stop),
                userInterface);

        _fragment.OnPrevious += () =>
            Send(
                new PipBoyRadioUiMessageEvent(
                    PipBoyRadioAction.Previous),
                userInterface);

        _fragment.OnNext += () =>
            Send(
                new PipBoyRadioUiMessageEvent(
                    PipBoyRadioAction.Next),
                userInterface);
    }

    public override void UpdateState(
        BoundUserInterfaceState state)
    {
        if (state is not PipBoyRadioUiState radioState)
            return;

        _fragment?.UpdateState(
            radioState.Songs,
            radioState.SelectedSongId?.Id,
            radioState.Playing,
            radioState.Paused);
    }

    private static void Send(
        PipBoyRadioUiMessageEvent radioMessage,
        BoundUserInterface userInterface)
    {
        userInterface.SendMessage(
            new CartridgeUiMessage(radioMessage));
    }
}
