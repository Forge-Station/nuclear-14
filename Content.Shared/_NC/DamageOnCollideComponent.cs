using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._RMC14.Fire;

/// <summary>
/// Наносит урон сущностям при коллизии с тайлом.
/// В отличие от RMCIgniteOnCollide — наносит урон напрямую, без поджога.
/// </summary>
[RegisterComponent]
public sealed partial class DamageOnCollideComponent : Component
{
    /// <summary>
    /// Урон наносимый при касании.
    /// Пример: Heat: 45
    /// </summary>
    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier Damage { get; set; } = new();

    /// <summary>
    /// Если true — урон наносится и мёртвым сущностям.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool DamageDead { get; set; } = false;

    /// <summary>
    /// Помечает урон как огневой.
    /// Может использоваться другими системами (например, для звука или эффектов).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Fire { get; set; } = false;

    /// <summary>
    /// Если true — игнорирует все сопротивления (resistances) при нанесении урона.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool IgnoreResistances { get; set; } = false;

    /// <summary>
    /// Накопитель времени для тика урона.
    /// Урон наносится раз в DamageInterval секунд, не при каждой физической итерации.
    /// </summary>
    [DataField]
    public float DamageAccumulator { get; set; } = 0f;

    /// <summary>
    /// Интервал между тиками урона в секундах.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DamageInterval { get; set; } = 1f;
}
