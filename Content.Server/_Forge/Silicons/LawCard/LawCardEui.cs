using Content.Server.EUI;
using Content.Shared._Forge.Silicons.LawCard;
using Content.Shared.Eui;

namespace Content.Server._Forge.Silicons.LawCard;

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
        return new LawCardEuiState(_system.GetLaws(_card), _system.GetOverwriteAccess(_card));
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is LawCardSaveMessage save && Player.AttachedEntity is { } user)
            _system.SaveLaws(_card, save.Laws, save.OverwriteAccess, user);
    }

    public override void Closed()
    {
        base.Closed();
        _system.OnEditorClosed(_card, this);
    }
}
