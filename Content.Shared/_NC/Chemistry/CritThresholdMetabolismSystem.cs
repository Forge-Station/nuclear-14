using Content.Server._NC.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;

namespace Content.Server._NC.Chemistry;

/// <summary>
/// Отвечает за откат порогов при удалении компонента <see cref="CritThresholdMetabolismComponent"/>.
/// </summary>
public sealed class CritThresholdMetabolismSystem : EntitySystem
{
    [Dependency] private readonly MobThresholdSystem _threshold = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CritThresholdMetabolismComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(EntityUid uid, CritThresholdMetabolismComponent comp, ComponentShutdown args)
    {
        RevertThreshold(uid, MobState.Critical, comp.CritModifier);
        RevertThreshold(uid, MobState.Dead, comp.DeadModifier);
    }

    private void RevertThreshold(EntityUid uid, MobState state, int modifier)
    {
        if (modifier == 0)
            return;

        var current = _threshold.GetThresholdForState(uid, state);
        if (current == FixedPoint2.Zero)
            return;

        _threshold.SetMobStateThreshold(uid, current - modifier, state);
    }
}