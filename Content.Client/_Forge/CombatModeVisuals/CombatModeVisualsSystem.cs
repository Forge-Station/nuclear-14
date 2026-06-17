using Content.Shared._Forge.CombatModeVisuals;
using Content.Shared.CombatMode;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.CombatModeVisuals;

// Показывает/прячет оверлей-слои спрайта (CombatModeVisuals.Layers) по боевому режиму.
// Опрашиваем в Update, а не подпиской: пару (CombatModeComponent, AfterAutoHandleStateEvent) уже занял ядровый
// CombatModeSystem, а второй подписки на ту же пару движок не допускает. Сущностей с компонентом единицы — дёшево.
public sealed class CombatModeVisualsSystem : EntitySystem
{
    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<CombatModeVisualsComponent, CombatModeComponent, SpriteComponent>();
        while (query.MoveNext(out _, out var visuals, out var combat, out var sprite))
        {
            if (visuals.LastInCombat == combat.IsInCombatMode)
                continue;

            visuals.LastInCombat = combat.IsInCombatMode;
            foreach (var key in visuals.Layers)
            {
                if (sprite.LayerMapTryGet(key, out var layer))
                    sprite.LayerSetVisible(layer, combat.IsInCombatMode);
            }
        }
    }
}
