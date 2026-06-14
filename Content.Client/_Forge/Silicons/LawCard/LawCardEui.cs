using Content.Client.Eui;
using Content.Shared._Forge.Silicons.LawCard;
using Content.Shared.Eui;

namespace Content.Client._Forge.Silicons.LawCard;

public sealed class LawCardEui : BaseEui
{
    private readonly LawCardWindow _window;

    public LawCardEui()
    {
        _window = new LawCardWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.Save.OnPressed += _ => SendMessage(new LawCardSaveMessage(_window.GetLaws()));
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not LawCardEuiState s)
            return;

        _window.SetLaws(s.Laws);
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }
}
