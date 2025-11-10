using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared._NC.Clouds;
using Robust.Shared.Random;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Maths;

namespace Content.Server._NC.Clouds;

/// <summary>
///     Ensures the cloud layer drifts with a consistent direction for everyone on the server.
/// </summary>
public sealed class NCCloudLayerSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private readonly HashSet<EntityUid> _trackedLayers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NCCloudLayerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<NCCloudLayerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NCCloudLayerComponent, MapInitEvent>(OnMapInit);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _trackedLayers.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_trackedLayers.Count == 0)
            return;

        var curTime = _timing.CurTime;

        var toRemove = new List<EntityUid>();

        foreach (var uid in _trackedLayers)
        {
            if (!TryComp(uid, out NCCloudLayerComponent? component))
            {
                toRemove.Add(uid);
                continue;
            }

            if (UpdateLayer(uid, component, curTime, frameTime))
                Dirty(uid, component);
        }

        foreach (var uid in toRemove)
        {
            _trackedLayers.Remove(uid);
        }
    }

    private void OnStartup(EntityUid uid, NCCloudLayerComponent component, ref ComponentStartup args)
    {
        component.IsActive = false;
        component.CurrentOpacity = 0f;
        component.ManualOverride = false;
        component.ManualEndTime = null;
        component.EventEndTime = null;
        component.NextEventStart = null;
        component.Phase = NCCloudLayerPhase.Inactive;

        var curTime = _timing.CurTime;

        if (component.StartActive)
        {
            StartClouds(uid, component, curTime, null, false);
        }
        else
        {
            ScheduleNextAutomatic(component, curTime);
        }

        _trackedLayers.Add(uid);
        Dirty(uid, component);
    }

    private void OnShutdown(EntityUid uid, NCCloudLayerComponent component, ref ComponentShutdown args)
    {
        _trackedLayers.Remove(uid);
    }

    private void OnMapInit(EntityUid uid, NCCloudLayerComponent component, ref MapInitEvent args)
    {
        // Intentionally left blank; drift is randomised when cloud events actually start.
        component.CurrentOpacity = 0f;
        component.IsActive = false;
    }

    private bool UpdateLayer(EntityUid uid, NCCloudLayerComponent component, TimeSpan curTime, float frameTime)
    {
        var dirty = false;

        switch (component.Phase)
        {
            case NCCloudLayerPhase.Inactive:
                if (!MathHelper.CloseTo(component.CurrentOpacity, 0f))
                {
                    component.CurrentOpacity = 0f;
                    dirty = true;
                }

                if (component.IsActive)
                {
                    component.IsActive = false;
                    dirty = true;
                }

                if (component.ManualOverride)
                {
                    StartClouds(uid, component, curTime, component.ManualEndTime, true);
                    dirty = true;
                }
                else if (component.NextEventStart is { } next && curTime >= next)
                {
                    StartClouds(uid, component, curTime, null, false);
                    dirty = true;
                }
                break;

            case NCCloudLayerPhase.FadingIn:
                dirty |= UpdateFade(component, frameTime, component.FadeInSeconds, increasing: true);

                if (MathHelper.CloseTo(component.CurrentOpacity, 1f) || component.FadeInSeconds <= 0f)
                {
                    component.CurrentOpacity = 1f;
                    component.Phase = NCCloudLayerPhase.Active;
                    dirty = true;
                }
                break;

            case NCCloudLayerPhase.Active:
                component.CurrentOpacity = 1f;

                if (component.ManualOverride)
                {
                    if (component.ManualEndTime is { } manualEnd && curTime >= manualEnd)
                    {
                        component.ManualOverride = false;
                        component.ManualEndTime = null;
                        BeginFadeOut(component);
                        dirty = true;
                    }
                }
                else if (component.EventEndTime is { } end && curTime >= end)
                {
                    BeginFadeOut(component);
                    dirty = true;
                }
                break;

            case NCCloudLayerPhase.FadingOut:
                dirty |= UpdateFade(component, frameTime, component.FadeOutSeconds, increasing: false);

                if (component.FadeOutSeconds <= 0f || component.CurrentOpacity <= 0f)
                {
                    component.CurrentOpacity = 0f;
                    component.IsActive = false;
                    component.Phase = NCCloudLayerPhase.Inactive;
                    component.EventEndTime = null;

                    if (!component.ManualOverride)
                    {
                        ScheduleNextAutomatic(component, curTime);
                    }

                    dirty = true;
                }
                break;
        }

        return dirty;
    }

    private void StartClouds(EntityUid uid, NCCloudLayerComponent component, TimeSpan curTime, TimeSpan? manualEnd, bool manualOverride)
    {
        component.Phase = NCCloudLayerPhase.FadingIn;
        component.IsActive = true;
        component.EventEndTime = manualOverride ? manualEnd : ChooseEventEnd(curTime, component);
        component.ManualOverride = manualOverride;
        component.ManualEndTime = manualEnd;
        component.NextEventStart = null;
        component.CurrentOpacity = 0f;

        if (component.RandomizeOnInit)
        {
            component.DriftPerSecond = PickRandomVector(component);
        }
    }

    private void BeginFadeOut(NCCloudLayerComponent component)
    {
        component.Phase = NCCloudLayerPhase.FadingOut;
        component.EventEndTime = null;
    }

    private TimeSpan? ChooseEventEnd(TimeSpan curTime, NCCloudLayerComponent component)
    {
        var durationSeconds = PickRange(component.MinActiveDuration, component.MaxActiveDuration);
        if (durationSeconds <= 0f)
            return null;

        return curTime + TimeSpan.FromSeconds(durationSeconds);
    }

    private void ScheduleNextAutomatic(NCCloudLayerComponent component, TimeSpan curTime)
    {
        var downtime = PickRange(component.MinDowntime, component.MaxDowntime);
        if (downtime <= 0f)
        {
            component.NextEventStart = curTime;
            return;
        }

        component.NextEventStart = curTime + TimeSpan.FromSeconds(downtime);
    }

    private Vector2 PickRandomVector(NCCloudLayerComponent component)
    {
        var angle = _random.NextFloat(0f, MathF.Tau);
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        var magnitude = component.DriftSpeed;
        if (component.DriftSpeedVariance > 0f)
        {
            magnitude += _random.NextFloat(-component.DriftSpeedVariance, component.DriftSpeedVariance);
            magnitude = MathF.Max(0f, magnitude);
        }

        return direction * magnitude;
    }

    private float PickRange(float min, float max)
    {
        if (max <= 0f && min <= 0f)
            return 0f;

        if (max < min)
            (min, max) = (max, min);

        if (MathHelper.CloseTo(min, max))
            return MathF.Max(0f, min);

        return _random.NextFloat(min, max);
    }

    private bool UpdateFade(NCCloudLayerComponent component, float frameTime, float duration, bool increasing)
    {
        if (duration <= 0f)
        {
            component.CurrentOpacity = increasing ? 1f : 0f;
            return true;
        }

        var delta = frameTime / duration;
        var previous = component.CurrentOpacity;
        component.CurrentOpacity = MathHelper.Clamp01(component.CurrentOpacity + (increasing ? delta : -delta));
        component.IsActive = true;
        return !MathHelper.CloseTo(previous, component.CurrentOpacity);
    }

    public bool TryGetCloudLayer(MapId mapId, out EntityUid uid, out NCCloudLayerComponent component)
    {
        component = default!;
        uid = default;

        if (!_mapSystem.TryGetMap(mapId, out var mapUidNullable) || mapUidNullable == null)
            return false;

        var mapUid = mapUidNullable.Value;

        if (!TryComp(mapUid, out NCCloudLayerComponent? found) || found == null)
            return false;

        component = found;
        uid = mapUid;
        return true;
    }

    public void ForceStartClouds(EntityUid uid, NCCloudLayerComponent component, TimeSpan? duration)
    {
        component.ManualOverride = true;
        component.ManualEndTime = duration.HasValue ? _timing.CurTime + duration : null;
        component.NextEventStart = null;
        StartClouds(uid, component, _timing.CurTime, component.ManualEndTime, true);
        Dirty(uid, component);
    }

    public void ForceStopClouds(EntityUid uid, NCCloudLayerComponent component)
    {
        component.ManualOverride = false;
        component.ManualEndTime = null;

        if (component.Phase == NCCloudLayerPhase.FadingOut || component.Phase == NCCloudLayerPhase.Inactive)
        {
            ScheduleNextAutomatic(component, _timing.CurTime);
            Dirty(uid, component);
            return;
        }

        BeginFadeOut(component);
        Dirty(uid, component);
    }
}
