using Content.Server.GameTicking;
using Content.Shared._Forge.WorldTime;

namespace Content.Server._Forge.WorldTime;

// Emits a round-wide event whenever the displayed time reaches a new minute.
public sealed class WorldTimeEventSystem : EntitySystem
{
    [Dependency] private readonly WorldTimeSystem _worldTime = default!;

    private const int MinutesPerDay = 24 * 60;

    private int? _lastMinute;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(_ => _lastMinute = null);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_worldTime.TryGetTimeOfDay(out var timeOfDay))
            return;

        var currentMinute = (int) timeOfDay.TotalMinutes;

        if (_lastMinute == null)
        {
            _lastMinute = currentMinute;
            return;
        }

        var elapsedMinutes = (currentMinute - _lastMinute.Value + MinutesPerDay) % MinutesPerDay;

        // A backwards jump is most likely a round/map reset. Re-sync without
        // emitting a large burst of events for every skipped minute.
        if (elapsedMinutes > MinutesPerDay / 2)
        {
            _lastMinute = currentMinute;
            return;
        }

        for (var offset = 1; offset <= elapsedMinutes; offset++)
        {
            var reachedMinute = (_lastMinute.Value + offset) % MinutesPerDay;

            RaiseLocalEvent(new WorldTimeReachedEvent(TimeSpan.FromMinutes(reachedMinute)));
        }

        _lastMinute = currentMinute;
    }
}
