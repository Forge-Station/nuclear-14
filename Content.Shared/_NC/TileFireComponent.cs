using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._RMC14.Fire;

/// <summary>
/// Компонент тайлового огня. Управляет временем жизни тайла,
/// способами тушения и визуальными стаками (1-4).
/// </summary>
[RegisterComponent]
public sealed partial class TileFireComponent : Component
{
    /// <summary>
    /// ID прототипа этого тайла. Используется при спавне огня
    /// чтобы знать какой именно тип тайла воспроизводить.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string? Id { get; set; }

    /// <summary>
    /// Длительность жизни тайла в секундах.
    /// По умолчанию 10 секунд.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Duration { get; set; } = 10f;

    /// <summary>
    /// Если true — тушится мгновенно от любого воздействия (вода, пена, PAT).
    /// Если false — используются множители ниже.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool ExtinguishInstantly { get; set; } = false;

    /// <summary>
    /// Множитель скорости тушения от PAT (противопожарная пена/система).
    /// 0 = нельзя потушить PAT-ом совсем.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float PatExtinguishMultiplier { get; set; } = 1f;

    /// <summary>
    /// Множитель скорости тушения от ручного распылителя (fire extinguisher, water).
    /// 0 = нельзя потушить вручную совсем.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float SprayExtinguishMultiplier { get; set; } = 1f;

    /// <summary>
    /// Урон наносимый структурам (стенам, дверям, машинам) рядом с тайлом.
    /// Применяется раз в StructureDamageInterval секунд.
    /// Если null — структурам урон не наносится.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier? StructureDamage { get; set; }

    /// <summary>
    /// Интервал между тиками урона по структурам в секундах.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float StructureDamageInterval { get; set; } = 2f;

    /// <summary>
    /// Накопитель времени для урона по структурам. Runtime-only.
    /// </summary>
    [DataField]
    public float StructureDamageAccumulator { get; set; } = 0f;

    /// <summary>
    /// Оставшееся время жизни тайла. Runtime-only.
    /// </summary>
    [DataField]
    public float TimeRemaining { get; set; } = -1f; // -1 = не инициализировано

    /// <summary>
    /// Текущий визуальный стак (1-4). Влияет на спрайт через GenericVisualizer.
    /// </summary>
    [DataField]
    public TileFireVisualStack CurrentStack { get; set; } = TileFireVisualStack.Four;
}

/// <summary>
/// Визуальные стаки огня. Соответствуют состояниям спрайта: red_1..red_4 и т.д.
/// </summary>
[Serializable, NetSerializable]
public enum TileFireVisualStack : byte
{
    One   = 0,
    Two   = 1,
    Three = 2,
    Four  = 3,
}

/// <summary>
/// Слои для GenericVisualizer. Должны совпадать с тем что в YAML:
/// enum.TileFireLayers.Base
/// </summary>
[Serializable, NetSerializable]
public enum TileFireLayers : byte
{
    Base,
}

[RegisterComponent]
public sealed partial class IgnorePredictionHitComponent : Component
{
}


[RegisterComponent]
public sealed partial class ExtinguisherSprayComponent : Component
{
}
