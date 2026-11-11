using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Inventory;

namespace Content.Server._Forge.Fire;

/// <summary>
/// Describes one application of heat from a fire source.
/// Sources decide where exposure happens; this value decides how strongly it affects an entity.
/// </summary>
public readonly record struct FireExposure(
    DamageSpecifier Damage,
    float FireStacks,
    float Intensity = 1f,
    bool Ignite = true,
    bool ApplyDamage = true,
    float IgnitionThreshold = 0f,
    bool IgnoreFireProtection = false);

/// <summary>
/// Central adapter between new fire sources and Nuclear-14's existing entity fire and damage systems.
/// </summary>
public sealed class FireExposureSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public bool ExposeEntity(
        EntityUid target,
        EntityUid ignitionSource,
        in FireExposure exposure,
        EntityUid? responsible = null)
    {
        if (Deleted(target) || exposure.Intensity <= 0f)
            return false;

        var affected = false;
        if (TryComp<FlammableComponent>(target, out var flammable))
        {
            var stacks = exposure.FireStacks * exposure.Intensity;
            if (stacks != 0f)
            {
                _flammable.AdjustFireStacks(target, stacks, flammable);
                affected = true;
            }

            if (exposure.Ignite &&
                (exposure.IgnitionThreshold <= 0f || flammable.FireStacks >= exposure.IgnitionThreshold))
            {
                _flammable.Ignite(target, ignitionSource, flammable, responsible);
                affected = true;
            }
        }

        if (exposure.ApplyDamage && HasComp<DamageableComponent>(target))
        {
            var origin = responsible is { } candidate && Exists(candidate)
                ? candidate
                : ignitionSource;
            var fireProtectionMultiplier = 1f;
            if (!exposure.IgnoreFireProtection)
            {
                var protection = new GetFireProtectionEvent();
                RaiseLocalEvent(target, ref protection);
                if (TryComp<InventoryComponent>(target, out var inventory))
                    _inventory.RelayEvent((target, inventory), ref protection);

                fireProtectionMultiplier = Math.Max(0f, protection.Multiplier);
            }

            _damageable.TryChangeDamage(
                target,
                exposure.Damage * exposure.Intensity * fireProtectionMultiplier,
                ignoreResistances: false,
                origin: origin,
                interruptsDoAfters: false);
            affected = true;
        }

        return affected;
    }
}
