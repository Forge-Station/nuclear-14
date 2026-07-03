using Content.Shared._Forge.ColdAura;
using Robust.Shared.GameObjects;              // EntitySystem
using Robust.Shared.IoC;
using Robust.Shared.Map;                      // TransformComponent

// Температура из вашего форка (как в AdjustTemperature)
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;

// Система модификаторов скорости и событие
using Content.Shared.Movement.Systems;

namespace Content.Server._Forge.ColdAura;

public sealed class ColdAuraSystem : EntitySystem
{
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ColdChillComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(EntityUid uid, ColdChillComponent comp, ref RefreshMovementSpeedModifiersEvent args)
    {
        // В вашем эвенте сеттеры приватные — используем ModifySpeed
        args.ModifySpeed(comp.Walk, comp.Sprint);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // 1) Тик аур: охлаждаем и навешиваем/обновляем замедление
        var auras = EntityQueryEnumerator<ColdAuraComponent, TransformComponent>();
        while (auras.MoveNext(out var auraUid, out var aura, out var ax))
        {
            if (!aura.Enabled)
                continue;

            aura.Accumulator += frameTime;
            if (aura.Accumulator < aura.UpdateInterval)
                continue;

            var dt = aura.Accumulator;
            aura.Accumulator = 0f;

            var heatDelta = aura.TemperatureChangePerSecond * dt; // отрицательное = холод
            if (heatDelta == 0f)
                continue;

            var srcPos = ax.WorldPosition;
            var range2 = aura.Range * aura.Range;

            var targets = EntityQueryEnumerator<TemperatureComponent, TransformComponent>();
            while (targets.MoveNext(out var targetUid, out var temp, out var tx))
            {
                var tPos = tx.WorldPosition;
                if ((tPos - srcPos).LengthSquared() > range2)
                    continue;

                // Не охлаждаем ниже минимальной температуры
                if (heatDelta < 0f && temp.CurrentTemperature <= aura.MinTemperature)
                    continue;

                // Понижаем/повышаем температуру так же, как в AdjustTemperature
                _temperature.ChangeHeat(targetUid, heatDelta, true, temp);

                // Замедление
                if (!aura.ApplySlow)
                    continue;

                if (!TryComp<ColdChillComponent>(targetUid, out var chill))
                    chill = AddComp<ColdChillComponent>(targetUid);

                chill.Walk = aura.WalkSpeedModifier;
                chill.Sprint = aura.SprintSpeedModifier;
                chill.TimeLeft = aura.UpdateInterval + aura.SlowLinger;
            }
        }

        // 2) Таймер замедления — снимаем, когда вышло время
        var slowed = EntityQueryEnumerator<ColdChillComponent>();
        while (slowed.MoveNext(out var uid, out var comp))
        {
            comp.TimeLeft -= frameTime;
            if (comp.TimeLeft <= 0f)
                RemComp<ColdChillComponent>(uid);
        }
    }
}
