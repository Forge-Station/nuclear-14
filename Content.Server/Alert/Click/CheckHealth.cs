using Content.Server.Chat.Managers;
using Content.Shared._Forge.Alert;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.HealthExaminable;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server.Alert.Click;

/// <summary>
/// Показывает состояние здоровья в чат по нажатию на алерт.
/// </summary>
public sealed class CheckHealthAlertSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly HealthExaminableSystem _healthExaminable = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HealthExaminableComponent, CheckHealthAlertEvent>(OnCheckHealth);
    }

    private void OnCheckHealth(Entity<HealthExaminableComponent> ent, ref CheckHealthAlertEvent args)
    {
        if (!TryComp<DamageableComponent>(ent, out var damageable)
            || !_player.TryGetSessionByEntity(ent, out var session))
            return;

        SendMessage(Loc.GetString("health-alert-start"), session);
        SendMessage(_healthExaminable.GetMarkup(ent, (ent.Owner, ent.Comp), damageable).ToMarkup(), session);
        args.Handled = true;
    }

    private void SendMessage(string msg, ICommonSession session)
    {
        _chat.ChatMessageToOne(ChatChannel.Emotes,
            msg,
            msg,
            EntityUid.Invalid,
            false,
            session.Channel);
    }
}
