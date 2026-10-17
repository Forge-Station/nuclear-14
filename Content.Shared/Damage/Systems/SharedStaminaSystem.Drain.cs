using Content.Shared.Damage.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared.Damage.Systems;

/// <summary>
/// Форковая часть: непрерывный расход стамины от источника (полёт, способности).
/// Апстрим такого не умеет - у него стамина только тратится разово и
/// восстанавливается сама.
/// </summary>
public partial class SharedStaminaSystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _drainMovementSpeed = default!;

    /// <summary>
    /// Включает или выключает расход стамины от конкретного источника.
    /// </summary>
    /// <param name="modifiesSpeed">Обновлять ли скорость: без этого расход не замедляет на месте.</param>
    public void ToggleStaminaDrain(EntityUid target, float drainRate, bool enabled, bool modifiesSpeed, EntityUid? source = null)
    {
        if (!TryComp<StaminaComponent>(target, out var stamina))
            return;

        // Если источник не указан, считаем, что цель расходует стамину сама.
        var actualSource = source ?? target;

        if (enabled)
        {
            stamina.ActiveDrains[actualSource] = (drainRate, modifiesSpeed);
            EnsureComp<ActiveStaminaComponent>(target);
        }
        else
        {
            stamina.ActiveDrains.Remove(actualSource);
        }

        Dirty(target, stamina);
    }

    /// <summary>
    /// Снимает стамину по всем активным источникам. Вызывается из Update.
    /// </summary>
    private void ApplyActiveDrains(EntityUid uid, StaminaComponent component, float frameTime)
    {
        foreach (var (source, (drainRate, modifiesSpeed)) in component.ActiveDrains)
        {
            TakeStaminaDamage(uid, drainRate * frameTime, component, source: source, visual: false);

            if (modifiesSpeed)
                _drainMovementSpeed.RefreshMovementSpeedModifiers(uid);
        }
    }
}
