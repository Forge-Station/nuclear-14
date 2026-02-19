using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._RMC14.Fire;

/// <summary>
/// При спавне тайла огня рисует обгоревший декаль на полу под ним.
/// </summary>
[RegisterComponent]
public sealed partial class RMCScorchEffectOnSpawnComponent : Component
{
    /// <summary>
    /// Тег декаля из реестра декалей.
    /// Например "burnt" — выжженное пятно.
    /// </summary>
    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public string DecalTag { get; set; } = "burnt";

    /// <summary>
    /// Максимальное количество декалей на одной клетке.
    /// Если уже столько есть — новый не рисуется.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int TileLimit { get; set; } = 1;
}
