using Robust.Shared.Configuration;

namespace Content.Shared._Forge.ResourceExtractor;

/// <summary>
/// Server-wide safety budgets for resource extractors.
/// </summary>
[CVarDefs]
public sealed class ResourceExtractorCVars
{
    /// <summary>
    /// Maximum number of output transactions processed across all extractors per server tick.
    /// </summary>
    public static readonly CVarDef<int> MaxOutputOperationsPerTick =
        CVarDef.Create("resource_extractor.max_output_operations_per_tick", 8, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of entities that one production cycle may roll before insertion.
    /// </summary>
    public static readonly CVarDef<int> MaxAllowedBatchSize =
        CVarDef.Create("resource_extractor.max_allowed_batch_size", 32, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of output entities inserted by all extractors per server tick.
    /// </summary>
    public static readonly CVarDef<int> MaxSpawnedEntitiesPerTick =
        CVarDef.Create("resource_extractor.max_spawned_entities_per_tick", 64, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of independent random events configured for one production cycle.
    /// </summary>
    public static readonly CVarDef<int> MaxEventsPerProduction =
        CVarDef.Create("resource_extractor.max_events_per_production", 4, CVar.SERVERONLY);

    /// <summary>
    /// Maximum total number of entities that all events may schedule per production cycle.
    /// </summary>
    public static readonly CVarDef<int> MaxEventSpawnsPerProduction =
        CVarDef.Create("resource_extractor.max_event_spawns_per_production", 8, CVar.SERVERONLY);

    /// <summary>
    /// Maximum number of completed telegraphs resolved across all extractors per server tick.
    /// </summary>
    public static readonly CVarDef<int> MaxEventOperationsPerTick =
        CVarDef.Create("resource_extractor.max_event_operations_per_tick", 8, CVar.SERVERONLY);

    /// <summary>
    /// Maximum event spawn radius from an extractor, in tiles.
    /// </summary>
    public static readonly CVarDef<int> MaxEventSpawnRadius =
        CVarDef.Create("resource_extractor.max_event_spawn_radius", 8, CVar.SERVERONLY);

    /// <summary>
    /// Maximum duration of an event telegraph, in seconds.
    /// </summary>
    public static readonly CVarDef<float> MaxTelegraphDuration =
        CVarDef.Create("resource_extractor.max_telegraph_duration", 10f, CVar.SERVERONLY);

    /// <summary>
    /// Minimum allowed extraction-cycle duration, in seconds.
    /// </summary>
    public static readonly CVarDef<float> MinExtractionDuration =
        CVarDef.Create("resource_extractor.min_extraction_duration", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Delay between output-capacity retries, in seconds.
    /// </summary>
    public static readonly CVarDef<float> OutputRetryPeriod =
        CVarDef.Create("resource_extractor.output_retry_period", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Delay between extractor UI state updates while running, in seconds.
    /// </summary>
    public static readonly CVarDef<float> UiUpdatePeriod =
        CVarDef.Create("resource_extractor.ui_update_period", 1f, CVar.SERVERONLY);

    /// <summary>
    /// Fuel amounts at or below this value are treated as zero, in solution units.
    /// </summary>
    public static readonly CVarDef<float> FuelEpsilon =
        CVarDef.Create("resource_extractor.fuel_epsilon", 0.001f, CVar.SERVERONLY);
}
