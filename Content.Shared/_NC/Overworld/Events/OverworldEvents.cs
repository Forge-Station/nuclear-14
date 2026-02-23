using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.Overworld.Events;

// ─── Сетевые (клиент → сервер) ────────────────────────────────────────────────

[Serializable, NetSerializable]
public sealed class EnterOverworldRequestEvent : EntityEventArgs
{
    public NetEntity Activator;
    public EnterOverworldRequestEvent(NetEntity activator) => Activator = activator;
}

[Serializable, NetSerializable]
public sealed class ExitOverworldRequestEvent : EntityEventArgs { }

// ─── Action event (Shared — нужен и серверу и клиенту) ───────────────────────

/// <summary>
/// Событие InstantAction "Выйти из Overworld".
/// Объявлено в Shared чтобы сервер мог подписаться на него.
/// Регистрируется в actions.yml как event: !type:ExitOverworldActionEvent
/// </summary>
public sealed partial class ExitOverworldActionEvent : InstantActionEvent { }

// ─── Серверные (локальные, для подписки других систем) ───────────────────────

/// <summary>Поднимается на теле после успешного входа в Overworld.</summary>
public sealed class PlayerEnteredOverworldEvent : EntityEventArgs
{
    public readonly EntityUid Body;
    public readonly EntityUid Token;

    public PlayerEnteredOverworldEvent(EntityUid body, EntityUid token)
    {
        Body = body;
        Token = token;
    }
}

/// <summary>Поднимается на теле после выхода из Overworld.</summary>
public sealed class PlayerExitedOverworldEvent : EntityEventArgs
{
    public readonly EntityUid Body;
    public readonly bool TraveledToLocation;

    public PlayerExitedOverworldEvent(EntityUid body, bool traveledToLocation = false)
    {
        Body = body;
        TraveledToLocation = traveledToLocation;
    }
}
