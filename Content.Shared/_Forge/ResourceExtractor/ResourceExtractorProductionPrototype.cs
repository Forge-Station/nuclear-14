using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.ResourceExtractor;

[Prototype]
public sealed partial class ResourceExtractorProductionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<EntityTablePrototype> OutputTable { get; private set; }

    /// <summary>
    /// Inclusive range defining how many times the output table is rolled per production cycle.
    /// </summary>
    [DataField]
    public int MinOutputRolls { get; private set; } = 1;

    [DataField]
    public int MaxOutputRolls { get; private set; } = 1;

    /// <summary>
    /// Hard limit for entity-table results returned by one production cycle.
    /// A single table roll may yield more than one result.
    /// </summary>
    [DataField]
    public int MaxOutputEntities { get; private set; } = 1;

    /// <summary>
    /// Independently rolled once for every completed, fuel-paid production cycle.
    /// </summary>
    [DataField]
    public List<ResourceExtractorSpawnEvent> Events { get; private set; } = new();
}

[DataDefinition]
public sealed partial class ResourceExtractorSpawnEvent
{
    [DataField(required: true)]
    public EntProtoId Entity { get; private set; }

    /// <summary>
    /// Exact number of entities spawned when this event roll succeeds.
    /// </summary>
    [DataField]
    public int Amount { get; private set; } = 1;

    [DataField]
    public float ChancePerRoll { get; private set; }

    [DataField(required: true)]
    public EntProtoId Telegraph { get; private set; }

    [DataField]
    public TimeSpan TelegraphDuration { get; private set; } = TimeSpan.FromSeconds(1);

    [DataField]
    public int SpawnRadius { get; private set; } = 2;
}
