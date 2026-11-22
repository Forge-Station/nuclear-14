using Content.Shared.Damage;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Fire;

/// <summary>
/// A server-authoritative fire occupying one grid tile.
/// </summary>
[RegisterComponent]
public sealed partial class SurfaceFireComponent : Component
{
    [DataField]
    public EntProtoId FirePrototype = "ForgeSurfaceFire";

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan InitialStageDuration = TimeSpan.FromSeconds(0.5);

    [DataField]
    public float Intensity = 1f;

    [DataField]
    public float FireStacksPerTick = 0.4f;

    /// <summary>
    /// Fire stacks contributed once per propagation step to combustible entities on neighboring tiles.
    /// The target's propagation profile still controls its multiplier, threshold, and per-step cap.
    /// </summary>
    [DataField]
    public float AdjacentFireStacks = 0.5f;

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// Contact damage is batched separately from fire-stack updates so that frequent tiny hits do
    /// not get completely erased by flat damage reductions on otherwise vulnerable structures.
    /// </summary>
    [DataField]
    public TimeSpan DamageInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Seconds removed from the remaining lifetime per unit of extinguishing reagent.
    /// </summary>
    [DataField]
    public float ExtinguishMultiplier = 2f;

    /// <summary>
    /// Exact reagents and reagent families allowed to fuel this surface fire. Other evaporable
    /// puddle contents still evaporate, but do not extend its lifetime.
    /// </summary>
    [DataField]
    public List<SurfaceFireFuelConfiguration> Fuels = new();

    /// <summary>
    /// Total units of non-fuel evaporable reagents removed from the puddle per second.
    /// Mixed solutions share this budget proportionally.
    /// </summary>
    [DataField]
    public float PuddleEvaporationPerSecond = 1f;

    /// <summary>
    /// If enabled, evaporated non-fuel reagents retain their ReagentId in the resulting cloud.
    /// </summary>
    [DataField]
    public bool CarryEvaporatedReagents = true;

    /// <summary>
    /// Fraction of evaporated non-fuel reagent transferred into the chemical cloud.
    /// </summary>
    [DataField]
    public float EvaporatedVaporFraction = 0.05f;

    /// <summary>
    /// Upper bound for the remaining lifetime after consuming puddle fuel.
    /// This prevents a large spill from banking an arbitrarily long-lived fire.
    /// </summary>
    [DataField]
    public TimeSpan MaxFuelledDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Delay between attempts to ignite fuel puddles on the four cardinal neighboring tiles.
    /// </summary>
    [DataField]
    public TimeSpan FuelSpreadInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum whitelisted fuel volume required on a neighboring tile for fire to spread there.
    /// </summary>
    [DataField]
    public float MinimumFuelToSpread = 0.25f;

    /// <summary>
    /// Initial lifetime of a fire created by fuel-to-fuel propagation. Fuel consumption can extend it.
    /// </summary>
    [DataField]
    public TimeSpan FuelSpreadDuration = TimeSpan.FromSeconds(3);

    [DataField]
    public float MinimumFuelIntensity = 0.5f;

    [DataField]
    public float MaximumFuelIntensity = 1.5f;

    [DataField]
    public EntProtoId VaporPrototype = "ForgeChemicalVapor";

    [DataField]
    public TimeSpan VaporEmissionInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public float MinimumVaporVolume = 0.1f;

    [DataField(required: true)]
    public DamageSpecifier DamagePerInterval = new();

    [ViewVariables]
    public TimeSpan SpawnedAt;

    [ViewVariables]
    public TimeSpan ExpiresAt;

    [ViewVariables]
    public TimeSpan NextTick;

    [ViewVariables]
    public TimeSpan NextDamage;

    [ViewVariables]
    public TimeSpan NextVaporEmission;

    [ViewVariables]
    public TimeSpan NextFuelSpread;

    [ViewVariables]
    public float CurrentFuelVolume;

    [ViewVariables]
    public float MediumFuelVolume;

    [ViewVariables]
    public float LargeFuelVolume;

    [ViewVariables]
    public float FuelIntensityMultiplier = 1f;

    [ViewVariables]
    public bool WasFuelled;

    [ViewVariables]
    public Solution PendingVapor = new(4);

    [ViewVariables]
    public EntityUid? Source;

    public float EffectiveIntensity => Intensity * FuelIntensityMultiplier;
}

/// <summary>
/// Describes one explicitly whitelisted puddle reagent that can sustain surface fire.
/// </summary>
[DataDefinition]
public sealed partial class SurfaceFireFuelConfiguration
{
    /// <summary>
    /// Exact reagent matched by this profile. Exact matches take priority over inherited matches.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Reagent;

    /// <summary>
    /// Optional abstract or concrete reagent ancestor. Allows one weak profile for a reagent family,
    /// such as every drink inheriting BaseAlcohol.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Parent;

    /// <summary>
    /// Units removed from the puddle per second while fire occupies its tile.
    /// </summary>
    [DataField]
    public float ConsumptionPerSecond = 1f;

    /// <summary>
    /// Seconds added to fire lifetime per consumed unit.
    /// </summary>
    [DataField]
    public float DurationPerUnit = 1f;

    /// <summary>
    /// If enabled, part of the actually consumed reagent is emitted as chemical vapor.
    /// </summary>
    [DataField]
    public bool CarryOriginalReagents;

    /// <summary>
    /// Fraction of consumed reagent transferred to vapor. The remainder is destroyed by combustion.
    /// </summary>
    [DataField]
    public float VaporFraction = 0.1f;
}

/// <summary>
/// Paints surface fire over every tile crossed by the entity carrying this component.
/// Intended for short-lived flamethrower projectiles.
/// </summary>
[RegisterComponent]
public sealed partial class SurfaceFireTrailComponent : Component
{
    [DataField]
    public EntProtoId FirePrototype = "ForgeSurfaceFire";

    [DataField]
    public TimeSpan? Duration;

    [DataField]
    public float? Intensity;

    /// <summary>
    /// Number of traversed tiles after the shooter's tile that must remain free of surface fire.
    /// </summary>
    [DataField]
    public int TilesToSkip = 1;

    /// <summary>
    /// Maximum number of surface-fire tiles painted after the safe distance.
    /// A non-positive value disables the limit.
    /// </summary>
    [DataField]
    public int MaxTiles = 8;

    [ViewVariables]
    public int RemainingTilesToSkip;

    [ViewVariables]
    public int PaintedTiles;

    [ViewVariables]
    public EntityUid? LastGrid;

    [ViewVariables]
    public Vector2i LastTile;

    [ViewVariables]
    public bool HasLastTile;
}

public enum SurfaceFirePatternShape : byte
{
    Point,
    Circle,
    Cone,
}

/// <summary>
/// Creates a prototype-configured surface-fire pattern when its owner receives a trigger event.
/// The trigger owner remains responsible for deciding when the event happens (impact, timer, destruction, etc.).
/// </summary>
[RegisterComponent]
public sealed partial class SurfaceFireOnTriggerComponent : Component
{
    [DataField]
    public EntProtoId FirePrototype = "ForgeSurfaceFire";

    [DataField]
    public TimeSpan? Duration;

    [DataField]
    public float? Intensity;

    [DataField]
    public SurfaceFirePatternShape Shape = SurfaceFirePatternShape.Point;

    /// <summary>
    /// Circle radius, or number of rows in a cone.
    /// </summary>
    [DataField]
    public int Radius = 1;

    /// <summary>
    /// Optional width of every cone row. Widths must be positive odd numbers.
    /// If omitted, the cone grows as 1, 3, 5, ... up to <see cref="Radius"/> rows.
    /// </summary>
    [DataField]
    public List<int>? RowWidths;

    /// <summary>
    /// Prevents fire from appearing behind walls and closed doors relative to the pattern origin.
    /// </summary>
    [DataField]
    public bool StopAtObstacles = true;
}

/// <summary>
/// Explicitly prevents a flamethrower trail from continuing past this anchored entity.
/// </summary>
[RegisterComponent]
public sealed partial class BlockSurfaceFireComponent : Component;

/// <summary>
/// Allows surface fire and flamethrower trails to pass through an entity that would otherwise
/// block them, such as a low wall carrying the generic wall tag.
/// </summary>
[RegisterComponent]
public sealed partial class SurfaceFirePassThroughComponent : Component;
