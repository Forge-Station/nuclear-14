using Content.Shared.Parallax.Biomes;
using Content.Shared.TimeCycle;
using Content.Shared.Weather;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;


namespace Content.Server._Forge.QuestInstance;

[Prototype("questInstancePreset")]
public sealed partial class QuestInstancePresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    [DataField]
    public ProtoId<BiomeTemplatePrototype>? BiomeTemplateId;

    [DataField]
    public int TimeLimitSeconds = 900;

    [DataField]
    public int BarrierPadding = 100;

    [DataField]
    public int MaxBarrierRadius = 256;

    [DataField]
    public List<QuestInstanceMapEntry> MapEntries = new();

    [DataField]
    public List<ResPath> MapPaths = new();

    [DataField]
    public ResPath? OptionalMapPath;

    [DataField]
    public string ExitSignpostProto = "QuestSignpost";

    [DataField]
    public string BarrierProto = "QuestInvisibleWall";

    [DataField]
    public float SpawnDistanceFromGrid = 3f;

    [DataField]
    public int[] WarningThresholdsSeconds = [60, 30, 10];

    [DataField]
    public int JoinWindowSeconds = 120;

    [DataField]
    public float AtmosphereTemperatureCelsius = 20f;
}

[DataDefinition]
public sealed partial class QuestInstanceMapEntry
{
    [DataField(required: true)]
    public ResPath MapPath = default!;

    [DataField]
    public float Weight = 1f;

    [DataField]
    public ProtoId<BiomeTemplatePrototype>? BiomeTemplateId;

    [DataField]
    public Color? MapLightColor;

    [DataField]
    public ProtoId<WeatherPrototype>? WeatherPrototypeId;

    [DataField]
    public ProtoId<TimeCyclePalettePrototype>? TimeCyclePaletteId;

    [DataField]
    public float? AtmosphereTemperatureCelsius;
}

