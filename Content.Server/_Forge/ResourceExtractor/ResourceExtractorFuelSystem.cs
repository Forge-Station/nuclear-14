using System.Linq;
using Content.Server.Power.Generator;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Materials;

namespace Content.Server._Forge.ResourceExtractor;

/// <summary>
/// Adapts the existing generator fuel contract for Forge machines that consume
/// fuel without producing electrical power.
/// </summary>
public sealed class ResourceExtractorFuelSystem : EntitySystem
{
    [Dependency] private readonly GeneratorSystem _generator = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public float GetFuel(EntityUid uid)
    {
        var fuel = _generator.GetFuel(uid);
        return float.IsFinite(fuel) ? MathF.Max(0f, fuel) : 0f;
    }

    public bool IsClogged(EntityUid uid)
    {
        return _generator.GetIsClogged(uid);
    }

    public bool HasFuel(EntityUid uid, float fuelEpsilon)
    {
        return GetFuel(uid) > fuelEpsilon;
    }

    /// <summary>
    /// Gets the physical fill level used by the UI and verifies that exactly
    /// one of the existing chemical or solid generator adapters is present.
    /// This avoids extending upstream generator code with extractor-only UI data.
    /// </summary>
    public bool TryGetFuelFraction(EntityUid uid, out float fraction)
    {
        fraction = 0f;
        var chemical = HasComp<ChemicalFuelGeneratorAdapterComponent>(uid);
        var solid = HasComp<SolidFuelGeneratorAdapterComponent>(uid);
        if (chemical == solid)
            return false;

        if (chemical)
        {
            if (!_solutions.TryGetRefillableSolution(uid, out _, out var solution) ||
                solution.MaxVolume <= 0)
            {
                return false;
            }

            fraction = Math.Clamp(solution.FillFraction, 0f, 1f);
            return float.IsFinite(fraction);
        }

        if (!TryComp<MaterialStorageComponent>(uid, out var storage) ||
            storage.StorageLimit is not { } limit ||
            limit <= 0)
        {
            return false;
        }

        var stored = storage.Storage.Values.Sum(value => (long) value);
        fraction = (float) Math.Clamp((double) stored / limit, 0d, 1d);
        return true;
    }

    public void EmptyFuel(EntityUid uid)
    {
        _generator.EmptyGenerator(uid);
    }

    public FuelConsumptionResult TryConsumeFuelForDuration(
        EntityUid uid,
        TimeSpan requestedDuration,
        float consumptionRate,
        float fuelEpsilon)
    {
        if (requestedDuration <= TimeSpan.Zero ||
            !float.IsFinite(consumptionRate) || consumptionRate <= 0f ||
            !float.IsFinite(fuelEpsilon) || fuelEpsilon < 0f)
        {
            return new FuelConsumptionResult(TimeSpan.Zero, FuelConsumptionStatus.InvalidConfiguration);
        }

        if (IsClogged(uid))
            return new FuelConsumptionResult(TimeSpan.Zero, FuelConsumptionStatus.Clogged);

        var fuelBefore = GetFuel(uid);
        if (fuelBefore <= fuelEpsilon)
            return new FuelConsumptionResult(TimeSpan.Zero, FuelConsumptionStatus.Exhausted);

        var requestedFuelDouble = requestedDuration.TotalSeconds * consumptionRate;
        if (!double.IsFinite(requestedFuelDouble) || requestedFuelDouble <= 0d)
            return new FuelConsumptionResult(TimeSpan.Zero, FuelConsumptionStatus.InvalidConfiguration);

        var requestedFuel = (float) Math.Min(requestedFuelDouble, float.MaxValue);
        var payableFuel = MathF.Min(fuelBefore, requestedFuel);
        RaiseLocalEvent(uid, new GeneratorUseFuel(payableFuel));

        // GeneratorUseFuel is a command rather than a result event. As in
        // GeneratorSystem, trust the adapter after GetFuel reported the amount
        // as available. Subtracting two large floats loses tiny frame burns.
        var workedSeconds = payableFuel / consumptionRate;
        workedSeconds = (float) Math.Clamp(workedSeconds, 0d, requestedDuration.TotalSeconds);

        var fuelAfter = GetFuel(uid);
        var exhausted = fuelAfter <= fuelEpsilon || payableFuel < requestedFuel;
        return new FuelConsumptionResult(
            TimeSpan.FromSeconds(workedSeconds),
            exhausted ? FuelConsumptionStatus.Exhausted : FuelConsumptionStatus.Worked);
    }
}

public readonly record struct FuelConsumptionResult(
    TimeSpan WorkedDuration,
    FuelConsumptionStatus Status);

public enum FuelConsumptionStatus : byte
{
    Worked,
    Exhausted,
    Clogged,
    InvalidConfiguration,
}
