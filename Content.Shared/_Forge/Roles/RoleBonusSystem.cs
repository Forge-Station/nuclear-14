using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Forge.Roles;

public sealed class RoleBonusSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly MobThresholdSystem _threshold = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoleBonusComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RoleBonusComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoleBonusComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<RoleBonusComponent, ModifySlowOnDamageSpeedEvent>(OnModifySlowOnDamage);
    }

    private void OnStartup(EntityUid uid, RoleBonusComponent component, ComponentStartup args)
    {
        if (_net.IsServer && component.HealthBonus != 0 && TryComp<MobThresholdsComponent>(uid, out _))
        {
            component.BaseCritThreshold = _threshold.GetThresholdForState(uid, MobState.Critical);
            _threshold.SetMobStateThreshold(uid,
                component.BaseCritThreshold + component.HealthBonus,
                MobState.Critical);
        }

        if (_net.IsServer && component.StaminaBonus != 0 && TryComp<StaminaComponent>(uid, out var stamina))
        {
            component.BaseStaminaThreshold = stamina.CritThreshold;
            stamina.CritThreshold += component.StaminaBonus;
            Dirty(uid, stamina);
        }

        _speed.RefreshMovementSpeedModifiers(uid);
    }

    private void OnShutdown(EntityUid uid, RoleBonusComponent component, ComponentShutdown args)
    {
        if (!_net.IsServer)
            return;

        if (component.HealthBonus != 0 && HasComp<MobThresholdsComponent>(uid))
            _threshold.SetMobStateThreshold(uid, component.BaseCritThreshold, MobState.Critical);

        if (component.StaminaBonus != 0 && TryComp<StaminaComponent>(uid, out var stamina))
        {
            stamina.CritThreshold = component.BaseStaminaThreshold;
            Dirty(uid, stamina);
        }
    }

    private void OnRefreshSpeed(EntityUid uid, RoleBonusComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(component.WalkSpeedMultiplier, component.SprintSpeedMultiplier);
    }

    private void OnModifySlowOnDamage(EntityUid uid, RoleBonusComponent component, ref ModifySlowOnDamageSpeedEvent args)
    {
        args.Speed = 1f - (1f - args.Speed) * component.SlowOnDamageModifier;
    }
}
