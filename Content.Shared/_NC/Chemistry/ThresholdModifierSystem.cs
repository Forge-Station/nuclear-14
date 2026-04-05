using System;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server.ThresholdModifier;

public sealed class ThresholdModifierSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly MobThresholdSystem _mobThreshold = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ThresholdModifierComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var currentTimeSeconds = _gameTiming.CurTime.TotalSeconds;
        var query = EntityQueryEnumerator<ThresholdModifierComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            var changed = false;
            // Удаляем истёкшие модификаторы (но их теперь не больше одного)
            for (var i = comp.Modifiers.Count - 1; i >= 0; i--)
            {
                if (comp.Modifiers[i].EndTimeSeconds <= currentTimeSeconds)
                {
                    comp.Modifiers.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                if (comp.Modifiers.Count == 0)
                {
                    RestoreOriginalThresholds(uid, comp);
                    RemComp<ThresholdModifierComponent>(uid);
                }
                else
                {
                    RecalculateAndApply(uid, comp);
                }
                Dirty(uid, comp);
            }
        }
    }

    private void OnShutdown(EntityUid uid, ThresholdModifierComponent comp, ComponentShutdown args)
    {
        RestoreOriginalThresholds(uid, comp);
    }

    public void ApplyTemporaryModifier(EntityUid uid, float critMultiplier, float deathMultiplier, TimeSpan duration)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        var comp = EnsureComp<ThresholdModifierComponent>(uid);
        var endTimeSeconds = (_gameTiming.CurTime + duration).TotalSeconds;

        // Убираем стакинг: удаляем все старые модификаторы, оставляем только один новый
        if (comp.Modifiers.Count > 0)
        {
            comp.Modifiers.Clear();
        }

        if (!comp.OriginalSaved)
        {
            _mobThreshold.TryGetThresholdForState(uid, MobState.Critical, out var crit, thresholds);
            _mobThreshold.TryGetThresholdForState(uid, MobState.Dead, out var dead, thresholds);
            comp.OriginalCritThreshold = crit ?? FixedPoint2.Zero;
            comp.OriginalDeadThreshold = dead ?? FixedPoint2.Zero;
            comp.OriginalSaved = true;
        }

        comp.Modifiers.Add(new ModifierEntry
        {
            CritMultiplier = critMultiplier,
            DeathMultiplier = deathMultiplier,
            EndTimeSeconds = endTimeSeconds
        });

        RecalculateAndApply(uid, comp);
        Dirty(uid, comp);
    }

    private void RecalculateAndApply(EntityUid uid, ThresholdModifierComponent comp)
    {
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        if (!comp.OriginalSaved)
            return;

        // Теперь в списке не более одного модификатора, но на всякий случай перемножаем все (их 0 или 1)
        float totalCritMult = 1f;
        float totalDeathMult = 1f;
        foreach (var mod in comp.Modifiers)
        {
            totalCritMult *= mod.CritMultiplier;
            totalDeathMult *= mod.DeathMultiplier;
        }

        if (!MathHelper.CloseTo(totalCritMult, 1f))
        {
            var newCrit = comp.OriginalCritThreshold * totalCritMult;
            _mobThreshold.SetMobStateThreshold(uid, newCrit, MobState.Critical, thresholds);
        }
        else
        {
            _mobThreshold.SetMobStateThreshold(uid, comp.OriginalCritThreshold, MobState.Critical, thresholds);
        }

        if (!MathHelper.CloseTo(totalDeathMult, 1f))
        {
            var newDead = comp.OriginalDeadThreshold * totalDeathMult;
            _mobThreshold.SetMobStateThreshold(uid, newDead, MobState.Dead, thresholds);
        }
        else
        {
            _mobThreshold.SetMobStateThreshold(uid, comp.OriginalDeadThreshold, MobState.Dead, thresholds);
        }
    }

    private void RestoreOriginalThresholds(EntityUid uid, ThresholdModifierComponent comp)
    {
        if (!comp.OriginalSaved)
            return;
        if (!TryComp<MobThresholdsComponent>(uid, out var thresholds))
            return;

        _mobThreshold.SetMobStateThreshold(uid, comp.OriginalCritThreshold, MobState.Critical, thresholds);
        _mobThreshold.SetMobStateThreshold(uid, comp.OriginalDeadThreshold, MobState.Dead, thresholds);
    }
}