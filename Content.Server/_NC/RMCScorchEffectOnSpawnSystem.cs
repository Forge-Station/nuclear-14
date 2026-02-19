using Content.Server.Decals;
using Content.Shared._RMC14.Fire;
using Content.Shared.Decals;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._RMC14.Fire;

/// <summary>
/// При спавне тайла огня рисует обгоревший декаль на полу.
/// </summary>
public sealed class RMCScorchEffectOnSpawnSystem : EntitySystem
{
    [Dependency] private readonly DecalSystem _decal = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RMCScorchEffectOnSpawnComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<RMCScorchEffectOnSpawnComponent> ent, ref ComponentInit args)
    {
        var comp = ent.Comp;
        var xform = Transform(ent);

        // Получаем координаты тайла
        var coords = _transform.GetMapCoordinates(ent, xform: xform);
        if (!_mapManager.TryFindGridAt(coords, out var gridUid, out var grid))
            return;

        var tileRef = grid.GetTileRef(grid.WorldToTile(coords.Position));
        var tileCenter = grid.GridTileToLocal(tileRef.GridIndices);

        // Считаем сколько уже декалей тега на этой клетке
        var existingDecals = _decal.GetDecalsInRange(gridUid, tileCenter.Position, 0.4f);
        int count = 0;
        foreach (var (_, decal) in existingDecals)
        {
            if (decal.Id == comp.DecalTag)
                count++;
        }

        if (count >= comp.TileLimit)
            return;

        // Рисуем декаль со случайным поворотом
        var angle = _random.NextFloat(0f, 360f);
        _decal.TryAddDecal(
            comp.DecalTag,
            tileCenter,
            out _,
            rotation: Angle.FromDegrees(angle),
            cleanable: true);
    }
}
