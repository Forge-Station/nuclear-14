using Content.Shared._Forge.CombatModeVisuals;
using Content.Shared.CombatMode;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.CombatModeVisuals;

// Показывает и прячет слои спрайта, когда существо входит/выходит из боевого режима.
// Проверяем каждый тик, а не через подписку: на смену боевого режима уже подписан движковый CombatModeSystem,
// а двух подписчиков на одно и то же событие движок не пускает. Существ с таким компонентом мало, так что не накладно.
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
