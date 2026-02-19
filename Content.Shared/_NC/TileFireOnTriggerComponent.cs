using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._RMC14.Fire;

/// <summary>
/// При детонации гранаты спавнит тайлы огня вокруг точки взрыва.
/// </summary>
[RegisterComponent]
public sealed partial class TileFireOnTriggerComponent : Component
{
    /// <summary>
    /// ID прототипа тайла огня который нужно заспавнить.
    /// Например: RMCTileFire, RMCTileFireGreen.
    /// </summary>
    [DataField(required: true), ViewVariables(VVAccess.ReadWrite)]
    public string Spawn { get; set; } = string.Empty;

    /// <summary>
    /// Радиус в тайлах в котором спавнится огонь.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int Radius { get; set; } = 2;

    /// <summary>
    /// Звук при срабатывании. Необязательный.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier? Sound { get; set; }
}
