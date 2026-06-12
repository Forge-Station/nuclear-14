using Content.Server.EUI;
using Content.Shared._Forge.Silicons;
using Content.Shared.Eui;

namespace Content.Server._Forge.Silicons;

/// <summary>
/// Forge-Change: server side of the law card editor EUI. State is read live from the card;
/// saving stores the edited laws back onto the card.
/// </summary>
public sealed class LawCardEui : BaseEui
{
    private readonly LawCardSystem _system;
    private readonly EntityUid _card;

    public LawCardEui(LawCardSystem system, EntityUid card)
    {
        _system = system;
        _card = card;
    }

    public override EuiStateBase GetNewState()
    {
        return new LawCardEuiState(_system.GetLaws(_card));
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is LawCardSaveMessage save)
            _system.SaveLaws(_card, save.Laws);
    }
}
