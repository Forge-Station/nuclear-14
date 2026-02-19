using Content.Server.Explosion.EntitySystems;
using Content.Shared._RMC14.Fire;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._RMC14.Fire;

/// <summary>
/// При детонации гранаты с TileFireOnTriggerComponent
/// спавнит тайлы огня вокруг точки взрыва.
/// </summary>
public sealed class TileFireOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TileFireOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<TileFireOnTriggerComponent> ent, ref TriggerEvent args)
    {
        var comp = ent.Comp;
        var coords = _transform.GetMapCoordinates(ent);

        // Играем звук если задан
        if (comp.Sound != null)
            _audio.PlayPvs(comp.Sound, ent);

        // Ищем грид под гранатой
        if (!_mapManager.TryFindGridAt(coords, out var gridUid, out var grid))
            return;

        // Спавним тайлы огня в радиусе
        var centerTile = grid.WorldToTile(coords.Position);

        for (var x = -comp.Radius; x <= comp.Radius; x++)
        {
            for (var y = -comp.Radius; y <= comp.Radius; y++)
            {
                // Круглая форма — отсекаем углы
                if (x * x + y * y > comp.Radius * comp.Radius)
                    continue;

                var tilePos = new Vector2i(centerTile.X + x, centerTile.Y + y);

                // Проверяем что тайл существует и не пустой
                var tileRef = grid.GetTileRef(tilePos);
                if (tileRef.Tile.IsEmpty)
                    continue;

                var tileCoords = grid.GridTileToLocal(tilePos);

                // Удаляем существующий огонь на этой клетке — не даём стаковаться
                foreach (var existing in _lookup.GetEntitiesInRange(tileCoords.ToMap(EntityManager, _transform), 0.3f))
                {
                    if (HasComp<TileFireComponent>(existing))
                        QueueDel(existing);
                }

                // Спавним тайл огня по центру клетки
                Spawn(comp.Spawn, tileCoords);
            }
        }
    }
}
