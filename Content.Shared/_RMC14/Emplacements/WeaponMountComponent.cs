using Content.Shared.Item;
using Content.Shared.Tools;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.WeaponMounts;

/// <summary>
///     Превращает сущность в турельный станок: к нему можно прикрепить оружие,
///     развернуть на карте и использовать как огневую точку.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true, raiseAfterAutoHandleState: true)]
[Access(typeof(SharedWeaponMountSystem))]
public sealed partial class WeaponMountComponent : Component

{
    // ── Задержки ──────────────────────────────────────────────────────────────

    [DataField, AutoNetworkedField]
    public TimeSpan AssembleDelay = TimeSpan.FromSeconds(1.5f);

    [DataField]
    public SoundSpecifier? AttachSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");

    /// <summary>Станок сломан и не может использоваться.</summary>
    [DataField, AutoNetworkedField]
    public bool Broken;

    // ── Поворот без инструмента ────────────────────────────────────────────────

    /// <summary>Если true, оператор может поворачивать станок без инструмента.</summary>
    [DataField, AutoNetworkedField]
    public bool CanRotateWithoutTool;

    // ── Звуки ─────────────────────────────────────────────────────────────────

    [DataField]
    public SoundSpecifier? DeploySound;

    [DataField]
    public SoundSpecifier? DetachSound = new SoundPathSpecifier("/Audio/Items/crowbar.ogg");

    [DataField, AutoNetworkedField]
    public TimeSpan DisassembleDelay = TimeSpan.FromSeconds(1.5f);

    /// <summary>Качество инструмента для снятия оружия со станка.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ToolQualityPrototype> DismantlingTool = "Prying";

    // ── Действие отстёгивания ─────────────────────────────────────────────────

    [DataField(customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string? DismountAction = "ActionDismount";

    [DataField, AutoNetworkedField]
    public EntityUid? DismountActionEntity;

    /// <summary>
    ///     Прототип оружия, которое создаётся внутри станка при инициализации карты.
    ///     Такое оружие нельзя снять игроком.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId? FixedWeaponPrototype;

    /// <summary>Оружие зафиксировано и не может быть снято.</summary>
    [DataField, AutoNetworkedField]
    public bool IsWeaponLocked;

    /// <summary>Оружие закреплено (затянуто инструментом) и станок готов к использованию.</summary>
    [DataField, AutoNetworkedField]
    public bool IsWeaponSecured;

    // ── Оружие ────────────────────────────────────────────────────────────────

    /// <summary>Белый список оружия, которое разрешено крепить. null = любое.</summary>
    [DataField, AutoNetworkedField]
    public EntityWhitelist? MountableWhitelist;

    /// <summary>Текущее прикреплённое оружие (если есть).</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? MountedEntity;

    /// <summary>Размер предмета с прикреплённым оружием.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ItemSizePrototype> MountedWeaponSize = "Huge";

    // ── Ограничения размещения ────────────────────────────────────────────────

    /// <summary>
    ///     Минимальное расстояние (в тайлах) до другого станка того же прототипа.
    ///     0 = без ограничений.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MountExclusionAreaSize = 5;

    // ── Развёртывание ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Если true, оператор автоматически пристёгивается к станку
    ///     сразу после его развёртывания (при наличии патронов).
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool MountOnDeploy;

    // ── Размеры предмета ──────────────────────────────────────────────────────

    /// <summary>Размер предмета без оружия.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ItemSizePrototype> MountSize = "Normal";

    [DataField]
    public SoundSpecifier? RotateSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");

    // ── Инструменты ───────────────────────────────────────────────────────────

    /// <summary>Качество инструмента для поворота станка.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<ToolQualityPrototype> RotationTool = "Anchoring";

    [DataField]
    public SoundSpecifier? SecureSound = new SoundPathSpecifier("/Audio/Items/deconstruct.ogg");

    [DataField]
    public SoundSpecifier? UndeploySound = new SoundPathSpecifier("/Audio/Items/screwdriver.ogg");

    // ── Пользователь ──────────────────────────────────────────────────────────

    /// <summary>Сущность, которая сейчас управляет станком (пристёгнута).</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? User;

    /// <summary>ID контейнера, в котором хранится прикреплённое оружие.</summary>
    [DataField, AutoNetworkedField]
    public string WeaponSlotId = "weapon";
}

// ── Визуальные слои ───────────────────────────────────────────────────────────

[Serializable, NetSerializable]
public enum WeaponMountLayers : byte
{
    /// <summary>Оружие видно, станок развёрнут.</summary>
    Deployed,

    /// <summary>Индикатор патронов в развёрнутом состоянии.</summary>
    DeployedAmmo,

    /// <summary>Оружие видно, станок сложен (транспортный режим).</summary>
    Folded,

    /// <summary>Индикатор патронов в сложенном состоянии.</summary>
    FoldedAmmo,

    /// <summary>Оверлей поломки.</summary>
    Broken,

    /// <summary>Свечение перегрева (прозрачность зависит от уровня тепла).</summary>
    Overheated
}
