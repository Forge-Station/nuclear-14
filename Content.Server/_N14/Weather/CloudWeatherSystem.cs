using Content.Server._NC.Clouds;
using Content.Shared._NC.Clouds;
using Content.Shared.GameTicking;
using Content.Shared.Weather;

namespace Content.Server._N14.Weather;

/// <summary>
/// Polls maps that have both <see cref="NCCloudLayerComponent"/> (with
/// <c>WeatherLinkEnabled = true</c>) and <see cref="WeatherComponent"/>.
/// Starts cloud cover during the weather's Starting/Running phases. Clouds
/// fade out when weather begins ending, before automatic scheduling resumes.
///
/// Only clouds this system triggered are stopped — auto-scheduled or
/// manually-triggered clouds are never interrupted.
/// </summary>
public sealed class CloudWeatherSystem : EntitySystem
{
    [Dependency] private readonly NCCloudLayerSystem _cloudLayer = default!;

    // Throttle: poll once per second rather than every tick.
    private float _accumulator;
    private const float Interval = 1f;

    /// <summary>Map entities whose cloud layer was started by this system.</summary>
    private readonly HashSet<EntityUid> _weatherTriggered = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _weatherTriggered.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < Interval)
            return;
        _accumulator -= Interval;
        _weatherTriggered.RemoveWhere(uid => !HasComp<NCCloudLayerComponent>(uid));

        var query = EntityQueryEnumerator<NCCloudLayerComponent, WeatherComponent>();
        while (query.MoveNext(out var uid, out var clouds, out var weather))
        {
            if (!clouds.WeatherLinkEnabled)
                continue;

            var weatherActive = IsWeatherRunning(weather);

            if (weatherActive)
            {
                // Weather is active — start clouds if they are idle and we haven't
                // started them yet (avoids double-triggering auto-running clouds).
                if (!_weatherTriggered.Contains(uid)
                    && (clouds.Phase == NCCloudLayerPhase.Inactive
                        || clouds.Phase == NCCloudLayerPhase.FadingOut))
                {
                    // null duration = indefinite; we control stop via weather end.
                    _cloudLayer.ForceStartClouds(uid, clouds, null);
                    _weatherTriggered.Add(uid);
                }
            }
            else
            {
                // Weather gone — only stop what WE started.
                if (!_weatherTriggered.Contains(uid))
                    continue;

                if (clouds.Phase == NCCloudLayerPhase.FadingIn
                    || clouds.Phase == NCCloudLayerPhase.Active)
                {
                    // ForceStopClouds clears ManualOverride and begins FadeOut;
                    // after fade-out completes the base system calls ScheduleNextAutomatic.
                    _cloudLayer.ForceStopClouds(uid, clouds);
                }

                _weatherTriggered.Remove(uid);
            }
        }
    }

    /// <summary>
    /// Returns true if at least one weather entry is in the Starting or Running state.
    /// </summary>
    private static bool IsWeatherRunning(WeatherComponent weather)
    {
        foreach (var (protoId, data) in weather.Weather)
        {
            if (protoId == "Default")
                continue;

            if (data.State == WeatherState.Starting || data.State == WeatherState.Running)
                return true;
        }
        return false;
    }
}
