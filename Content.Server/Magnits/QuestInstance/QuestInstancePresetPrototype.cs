using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Magnits.QuestInstance;

[Prototype("questInstancePreset")]
public sealed partial class QuestInstancePresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// BiomeTemplatePrototype ID to apply to the generated map.
    /// If mapPaths/optionalMapPath are also set, biome is still applied as a background.
    /// </summary>
    [DataField]
    public string? BiomeTemplateId;

    /// <summary>Seconds the instance lives before force-evacuation and deletion.</summary>
    [DataField]
    public int TimeLimitSeconds = 900;

    /// <summary>
    /// Extra tile-units added outside the grid AABB when computing the barrier ring radius.
    /// For a fresh biome map (empty grid) the radius is 1 + BarrierPadding.
    /// </summary>
    [DataField]
    public int BarrierPadding = 100;

    /// <summary>
    /// Hard cap for computed barrier radius to avoid spawning excessive wall entities.
    /// </summary>
    [DataField]
    public int MaxBarrierRadius = 256;

    /// <summary>
    /// List of pre-built map files (.yml) to pick randomly from on each instance creation.
    /// Takes priority over <see cref="OptionalMapPath"/>.
    /// </summary>
    [DataField]
    public List<ResPath> MapPaths = new();

    /// <summary>
    /// Single pre-built map file (.yml). Used when <see cref="MapPaths"/> is empty.
    /// </summary>
    [DataField]
    public ResPath? OptionalMapPath;

    /// <summary>Entity prototype spawned near the entry point as the exit signpost.</summary>
    [DataField]
    public string ExitSignpostProto = "QuestSignpost";

    /// <summary>Entity prototype used for each invisible barrier wall tile on the perimeter.</summary>
    [DataField]
    public string BarrierProto = "QuestInvisibleWall";

    /// <summary>
    /// Spawn offset (in tiles) from the loaded grid's right edge.
    /// Used so players appear near the grid, not directly on top of it.
    /// </summary>
    [DataField]
    public float SpawnDistanceFromGrid = 3f;

    /// <summary>
    /// Remaining-seconds thresholds at which popup warnings are sent to players.
    /// </summary>
    [DataField]
    public int[] WarningThresholdsSeconds = { 60, 30, 10 };

    /// <summary>
    /// Seconds after instance creation during which players not in Participants may still join.
    /// After this window, only returning participants are allowed.
    /// </summary>
    [DataField]
    public int JoinWindowSeconds = 120;
}

