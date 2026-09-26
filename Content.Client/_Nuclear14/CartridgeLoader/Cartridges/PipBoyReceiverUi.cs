using Content.Client.UserInterface.Fragments;
using Content.Shared._Nuclear14.CartridgeLoader.Cartridges;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._Nuclear14.CartridgeLoader.Cartridges;

public sealed partial class PipBoyReceiverUi : UIFragment
{
    private BoxContainer _root = default!;
    private BoxContainer _stations = default!;
    private Button _power = default!;
    private BoundUserInterface _userInterface = default!;
    private bool _enabled;

    public override Control GetUIFragmentRoot() => _root;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _userInterface = userInterface;
        _root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _root.AddChild(new Label { Text = Loc.GetString("pipboy-receiver-title") });
        _power = new Button();
        _power.OnPressed += _ => Send(new PipBoyReceiverUiMessageEvent(enabled: !_enabled));
        _root.AddChild(_power);
        _stations = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        _root.AddChild(_stations);
        _root.AddChild(new Label { Text = Loc.GetString("pipboy-receiver-speaker-hint") });
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not PipBoyReceiverUiState receiver)
            return;

        _enabled = receiver.Enabled;
        _power.Text = Loc.GetString(_enabled ? "pipboy-receiver-turn-off" : "pipboy-receiver-turn-on");
        _stations.RemoveAllChildren();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var channelId in receiver.Channels)
        {
            if (!prototypes.TryIndex(channelId, out var channel))
                continue;

            var button = new Button
            {
                Text = Loc.GetString("pipboy-receiver-station",
                    ("name", channel.LocalizedName), ("frequency", channel.Frequency)),
                ToggleMode = true,
                Pressed = channel.ID == receiver.SelectedChannel,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => Send(new PipBoyReceiverUiMessageEvent(channel: channelId));
            _stations.AddChild(button);
        }
    }

    private void Send(PipBoyReceiverUiMessageEvent message)
    {
        _userInterface.SendMessage(new CartridgeUiMessage(message));
    }
}
