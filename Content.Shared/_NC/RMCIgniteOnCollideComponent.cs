using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._RMC14.Fire;

/// <summary>
/// Поджигает сущности при коллизии с тайлом.
/// Также может наносить прямой урон от тайла (TileDamage) с учётом ArmorMultiplier.
/// </summary>
[RegisterComponent]
public sealed partial class RMCIgniteOnCollideComponent : Component
{
    /// <summary>
    /// Максимальное количество стаков огня, добавляемых при касании.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MaxStacks { get; set; } = 20;

    /// <summary>
    /// Интенсивность огня (влияет на скорость горения).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Intensity { get; set; } = 10f;

    /// <summary>
    /// Длительность горения в секундах.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Duration { get; set; } = 10f;

    /// <summary>
    /// Урон, наносимый тайлом напрямую по DamageableComponent.
    /// Применяется раз в DamageInterval секунд.
    /// Пример: Heat: 0.5
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier? TileDamage { get; set; }

    /// <summary>
    /// Цвет огня на сущности при поджоге.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public Color BurnColor { get; set; } = Color.Orange;

    /// <summary>
    /// Множитель урона для сущностей из ArmorWhitelist.
    /// Значение меньше 1.0 снижает урон (например 0.5 = -50%).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ArmorMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Белый список сущностей, к которым применяется ArmorMultiplier.
    /// Например только Xeno.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntityWhitelist? ArmorWhitelist { get; set; }

    /// <summary>
    /// Накопитель времени для тика TileDamage.
    /// Не сериализуется — runtime-only.
    /// </summary>
    [DataField]
    public float DamageAccumulator { get; set; } = 0f;

    /// <summary>
    /// Интервал между тиками TileDamage в секундах.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DamageInterval { get; set; } = 1f;

    /// <summary>
    /// Накопитель времени для повторного поджога.
    /// Нужен чтобы сущность снова загоралась если успела потухнуть.
    /// Не сериализуется — runtime-only.
    /// </summary>
    [DataField]
    public float IgniteAccumulator { get; set; } = 0f;

    /// <summary>
    /// Интервал повторного поджога в секундах.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float IgniteInterval { get; set; } = 1f;
}
