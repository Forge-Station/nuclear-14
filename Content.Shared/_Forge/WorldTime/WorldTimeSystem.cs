using System.Globalization;
using Content.Shared._NC14.DayNightCycle;
using Content.Shared.Examine;
using Content.Shared.Light.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.WorldTime;

// Exposes the main map's day/night phase as a round-wide time of day.
public sealed class WorldTimeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    private const int SecondsPerDay = 24 * 60 * 60;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WorldClockComponent, ExaminedEvent>(OnClockExamined);
    }

    private void OnClockExamined(Entity<WorldClockComponent> _, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!TryGetTimeOfDay(out var timeOfDay))
        {
            args.PushMarkup(Loc.GetString("world-clock-examine-unavailable"));
            return;
        }

        args.PushMarkup(Loc.GetString("world-clock-examine-time",
            ("time", FormatTimeOfDay(timeOfDay))));
    }

    // Tries to read the time of day from the map marked as the round's source.
    public bool TryGetTimeOfDay(out TimeSpan timeOfDay)
    {
        timeOfDay = default;

        var query = EntityQueryEnumerator<WorldTimeSourceComponent>();

        if (!query.MoveNext(out var uid, out var source))
            return false;

        var selection = TryComp<WorldTimeSettingsComponent>(uid, out var settings)
            ? settings.Cycle
            : WorldTimeCycle.Auto;

        var hasLight = TryComp<LightCycleComponent>(uid, out var light)
            && light.Enabled;

        if (selection == WorldTimeCycle.LightCycle
            || (selection == WorldTimeCycle.Auto && hasLight))
        {
            if (!hasLight)
                return false;

            // Use the same elapsed time and period as the client's lighting.
            // The lighting curve reaches its minimum at phase 0, maximum at 0.5.
            var elapsed = (float) (_timing.CurTime + light!.Offset - source.RoundStartTime).TotalSeconds;
            var duration = MathF.Max(1f, (float) light.Duration.TotalSeconds);

            if (!float.IsFinite(elapsed)
                || !float.IsFinite(duration))
                return false;

            timeOfDay = CyclePhaseToTimeOfDay(elapsed / duration);

            return true;
        }

        if (!TryComp<DayNightCycleComponent>(uid, out var dayNight))
            return false;

        timeOfDay = CyclePhaseToTimeOfDay(dayNight.CurrentCycleTime);

        return true;
    }

    // Converts a normalized day/night phase into a time within a 24-hour day.
    // Values outside 0..1 wrap around in the same way as the day/night cycle.
    public static TimeSpan CyclePhaseToTimeOfDay(float phase)
    {
        if (!float.IsFinite(phase))
            return TimeSpan.Zero;

        var normalized = phase - MathF.Floor(phase);
        var totalSeconds = (int) MathF.Floor(normalized * SecondsPerDay);
        totalSeconds = Math.Clamp(totalSeconds, 0, SecondsPerDay - 1);

        return TimeSpan.FromSeconds(totalSeconds);
    }

    // Formats a time within a day using the 24-hour clock.
    public static string FormatTimeOfDay(TimeSpan timeOfDay)
    {
        return timeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }
}
