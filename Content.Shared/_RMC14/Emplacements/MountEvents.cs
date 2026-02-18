using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;


namespace Content.Shared.WeaponMounts;


// ── Действия ─────────────────────────────────────────────────────────────────

/// <summary>Отстегнуть оператора от станка (кнопка действия).</summary>
public sealed partial class DismountActionEvent : InstantActionEvent { }

// ── Стрельба ─────────────────────────────────────────────────────────────────

/// <summary>
///     Аналог <c>AttemptShootEvent</c> для монтируемого оружия.
///     Содержит целевые координаты, которых нет в ванильном событии.
///     Поднимается системой <see cref="MountableWeaponSystem" /> перед каждым
///     выстрелом — подпишитесь на него, чтобы отменить или модифицировать выстрел.
/// </summary>
[ByRefEvent]
public record struct MountedWeaponShootAttemptEvent
{
    /// <summary>Установите true, чтобы отменить выстрел.</summary>
    public bool Cancelled;

    /// <summary>Куда целится оператор.</summary>
    public EntityCoordinates Target;

    /// <summary>Кто стреляет.</summary>
    public EntityUid User;

    /// <summary>Оружие на станке.</summary>
    public EntityUid Weapon;

    public MountedWeaponShootAttemptEvent(EntityUid user, EntityUid weapon, EntityCoordinates target)
    {
        User = user;
        Weapon = weapon;
        Target = target;
    }
}

// ── DoAfter: сборка / разборка ────────────────────────────────────────────────

/// <summary>Прикрепить оружие к станку.</summary>
[Serializable, NetSerializable,]
public sealed partial class AttachWeaponDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Снять оружие со станка.</summary>
[Serializable, NetSerializable,]
public sealed partial class DetachWeaponDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Зафиксировать оружие (подготовить к использованию).</summary>
[Serializable, NetSerializable,]
public sealed partial class SecureWeaponDoAfterEvent : SimpleDoAfterEvent;

// ── DoAfter: развёртывание ────────────────────────────────────────────────────

/// <summary>Развернуть станок из инвентаря на карту.</summary>
[Serializable, NetSerializable,]
public sealed partial class DeployMountDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Свернуть развёрнутый станок обратно в предмет.</summary>
[Serializable, NetSerializable,]
public sealed partial class UndeployMountDoAfterEvent : SimpleDoAfterEvent;

// ── DoAfter: ремонт ───────────────────────────────────────────────────────────

/// <summary>Починить сломанный станок сваркой.</summary>
[Serializable, NetSerializable,]
public sealed partial class RepairMountDoAfterEvent : SimpleDoAfterEvent;

[ByRefEvent]
public record struct GunCycleFireModeEvent(EntityUid User);
